using System;
using System.Threading;

namespace NetStatus
{
    /// <summary>后台线程定时读取 CPU 温度 / 占用率。</summary>
    internal sealed class CpuMonitor : IDisposable
    {
        private AppSettings _settings;
        private readonly ManualResetEvent _wake = new ManualResetEvent(false);
        private Thread _thread;
        private volatile bool _running;

        private CpuReading _current = new CpuReading();
        private bool _disposed;

        public CpuMonitor(AppSettings settings)
        {
            _settings = settings;
        }

        public event EventHandler<CpuReading> Updated;

        public CpuReading Current { get { return _current; } }

        public void Start()
        {
            if (_thread != null) { return; }
            _running = true;
            _thread = new Thread(new ThreadStart(Loop));
            _thread.IsBackground = true;
            _thread.Name = "NetStatusCpu";
            _thread.Start();
        }

        public void ApplySettings(AppSettings settings)
        {
            settings.Validate();
            _settings = settings;
            RequestSample();
        }

        public void RequestSample()
        {

            try { _wake.Set(); }
            catch (ObjectDisposedException) { }
        }

        public void Stop()
        {
            _running = false;
            RequestSample();
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

                if (settings.CpuSensor != CpuSensorKind.Off)
                {
                    CpuReading reading = Sample(settings);
                    _current = reading;
                    EventHandler<CpuReading> handler = Updated;
                    if (handler != null)
                    {
                        try { handler(this, reading); }
                        catch (Exception) { }
                    }
                }

                try { _wake.WaitOne(Math.Max(500, settings.CpuIntervalMs)); }
                catch (ObjectDisposedException) { break; }
                _wake.Reset();
            }
        }

        private static CpuReading Sample(AppSettings settings)
        {
            try
            {
                return settings.CpuSensor == CpuSensorKind.Load
                    ? CpuSensor.ReadLoad()
                    : CpuSensor.ReadTemperature();
            }
            catch (Exception ex)
            {
                CpuReading reading = new CpuReading();
                reading.Available = false;
                reading.Error = ex.Message;
                return reading;
            }
        }
    }
}
