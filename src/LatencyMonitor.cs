using System;
using System.Collections.Generic;
using System.Threading;

namespace NetStatus
{
    internal sealed class RoundSample
    {
        public int Rtt = -1;
        public int Success;
        public int Total;
    }

    internal sealed class MonitorUpdate
    {
        public ProbeResult Last;
        public LatencyLevel Level = LatencyLevel.Unknown;
        public bool Offline;
        public int AverageRtt = -1;
        public int MinRtt = -1;
        public int MaxRtt = -1;
        public int LossPercent;
        public int WindowSamples;
        public int ConsecutiveFailures;
        public string Note = "";
    }

    /// <summary>后台线程按固定间隔探测网络，并把判定结果抛给界面。</summary>
    internal sealed class LatencyMonitor : IDisposable
    {
        private AppSettings _settings;
        private readonly List<RoundSample> _history = new List<RoundSample>();
        private readonly ManualResetEvent _wake = new ManualResetEvent(false);
        private Thread _thread;
        private volatile bool _running;
        private volatile bool _paused;
        private volatile bool _probeOnce;
        private int _failStreak;
        private MonitorUpdate _current = new MonitorUpdate();
        private bool _disposed;

        public LatencyMonitor(AppSettings settings)
        {
            _settings = settings;
        }

        public event EventHandler<MonitorUpdate> Updated;

        public MonitorUpdate Current { get { return _current; } }

        public bool Paused { get { return _paused; } }

        public void Start()
        {
            if (_thread != null) { return; }
            _running = true;
            _thread = new Thread(new ThreadStart(Loop));
            _thread.IsBackground = true;
            _thread.Name = "NetStatusProbe";
            _thread.Start();
        }

        public void SetPaused(bool paused)
        {
            _paused = paused;
            if (!paused) { RequestProbe(); }
        }

        public void ApplySettings(AppSettings settings)
        {
            settings.Validate();
            _settings = settings;
            RequestProbe();
        }

        public void RequestProbe()
        {
            _probeOnce = true;
            try { _wake.Set(); }
            catch (ObjectDisposedException) { }
        }

        public void Stop()
        {
            _running = false;
            RequestProbe();
            Thread thread = _thread;
            _thread = null;
            if (thread != null && thread.IsAlive && thread != Thread.CurrentThread)
            {
                thread.Join(1200);
            }
        }

        public void Dispose()
        {
            if (_disposed) { return; }
            _disposed = true;
            Stop();
            _wake.Close();
        }

        private void Loop()
        {
            while (_running)
            {
                AppSettings settings = _settings;
                bool once = _probeOnce;
                _probeOnce = false;

                if (!_paused || once)
                {
                    try { Probe(settings); }
                    catch (Exception) { }
                }

                try { _wake.WaitOne(settings.IntervalMs); }
                catch (ObjectDisposedException) { break; }
                _wake.Reset();
            }
        }

        private void Probe(AppSettings settings)
        {
            ProbeResult result = LatencyProbe.Run(
                settings.Targets, settings.TimeoutMs, settings.TcpFallback, settings.TcpHost, settings.TcpPort);

            MonitorUpdate update = BuildUpdate(settings, result);
            _current = update;

            EventHandler<MonitorUpdate> handler = Updated;
            if (handler != null)
            {
                try { handler(this, update); }
                catch (Exception) { }
            }
        }

        private MonitorUpdate BuildUpdate(AppSettings settings, ProbeResult result)
        {
            if (result.HasResponse) { _failStreak = 0; }
            else { _failStreak = _failStreak + 1; }

            RoundSample round = new RoundSample();
            round.Rtt = result.BestRtt;
            round.Success = result.SuccessCount;
            round.Total = result.TotalCount;
            _history.Add(round);
            while (_history.Count > settings.HistorySize) { _history.RemoveAt(0); }

            MonitorUpdate update = new MonitorUpdate();
            update.Last = result;
            update.ConsecutiveFailures = _failStreak;

            int sum = 0, okRounds = 0, min = -1, max = -1, probes = 0, failures = 0;
            for (int i = 0; i < _history.Count; i++)
            {
                RoundSample item = _history[i];
                probes += item.Total;
                failures += item.Total - item.Success;
                if (item.Rtt >= 0)
                {
                    okRounds++;
                    sum += item.Rtt;
                    if (min < 0 || item.Rtt < min) { min = item.Rtt; }
                    if (item.Rtt > max) { max = item.Rtt; }
                }
            }

            update.WindowSamples = okRounds;
            update.MinRtt = min;
            update.MaxRtt = max;
            update.AverageRtt = okRounds > 0 ? sum / okRounds : -1;
            update.LossPercent = probes > 0 ? (failures * 100) / probes : 0;

            bool offline = !result.NetworkAvailable || _failStreak >= settings.FailThreshold;
            update.Offline = offline;

            int roundLoss = result.LossPercent;
            if (offline)
            {
                update.Level = LatencyLevel.Bad;
                update.Note = result.NetworkAvailable
                    ? "连续 " + _failStreak + " 次探测无响应"
                    : "系统未检测到可用网络";
            }
            else if (!result.HasResponse)
            {
                // 偶发一轮全部超时：先降级成橙色，避免误报成断网
                update.Level = LatencyLevel.Slow;
                update.Note = "本轮无响应（第 " + _failStreak + " 次）";
            }
            else if (result.BestRtt <= settings.FastMs && roundLoss <= settings.LossOrangePercent)
            {
                update.Level = LatencyLevel.Fast;
                update.Note = "延时 " + result.BestRtt + " ms";
            }
            else if (result.BestRtt <= settings.SlowMs && roundLoss < settings.LossRedPercent)
            {
                update.Level = LatencyLevel.Slow;
                update.Note = roundLoss > settings.LossOrangePercent
                    ? "延时 " + result.BestRtt + " ms，丢包 " + roundLoss + "%"
                    : "延时 " + result.BestRtt + " ms";
            }
            else
            {
                update.Level = LatencyLevel.Bad;
                update.Note = roundLoss >= settings.LossRedPercent
                    ? "严重丢包 " + roundLoss + "%"
                    : "延时过高 " + result.BestRtt + " ms";
            }

            return update;
        }
    }
}
