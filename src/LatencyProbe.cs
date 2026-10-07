using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Threading.Tasks;

namespace NetStatus
{
    internal sealed class ProbeSample
    {
        public string Target = "";
        public bool Success;
        public int Rtt = -1;
        public string Error = "";
    }

    internal sealed class ProbeResult
    {
        public bool NetworkAvailable = true;
        public int BestRtt = -1;          // 本轮最好的延时，-1 表示本轮没有任何响应
        public string BestTarget = "";
        public int SuccessCount;
        public int TotalCount;
        public string FailedTargets = "";
        public bool TcpFallbackUsed;
        public bool TcpReachable;
        public int TcpRtt = -1;
        public string TcpError = "";
        public int ElapsedMs;

        public bool HasResponse { get { return BestRtt >= 0; } }

        public int LossPercent
        {
            get
            {
                if (TotalCount <= 0) { return 0; }
                return ((TotalCount - SuccessCount) * 100) / TotalCount;
            }
        }
    }

    /// <summary>一轮探测：并行 ping 所有目标，取最快的一个；全都失败时可退回 TCP 连接测试。</summary>
    internal static class LatencyProbe
    {
        public static bool NetworkAvailable()
        {
            try { return NetworkInterface.GetIsNetworkAvailable(); }
            catch (Exception) { return true; }
        }

        public static ProbeResult Run(string[] targets, int timeoutMs, bool tcpFallback, string tcpHost, int tcpPort)
        {
            ProbeResult result = new ProbeResult();
            result.NetworkAvailable = NetworkAvailable();
            result.TotalCount = targets == null ? 0 : targets.Length;

            Stopwatch watch = Stopwatch.StartNew();
            List<Task<ProbeSample>> tasks = new List<Task<ProbeSample>>();
            if (targets != null)
            {
                for (int i = 0; i < targets.Length; i++)
                {
                    string target = targets[i];
                    int timeout = timeoutMs;
                    tasks.Add(Task.Factory.StartNew<ProbeSample>(delegate { return PingOnce(target, timeout); }));
                }
            }

            List<string> failed = new List<string>();
            for (int i = 0; i < tasks.Count; i++)
            {
                ProbeSample sample;
                try { sample = tasks[i].Result; }
                catch (Exception ex) { sample = Failed(targets[i], ex.Message); }

                if (sample.Success)
                {
                    result.SuccessCount++;
                    if (result.BestRtt < 0 || sample.Rtt < result.BestRtt)
                    {
                        result.BestRtt = sample.Rtt;
                        result.BestTarget = sample.Target;
                    }
                }
                else
                {
                    failed.Add(sample.Target);
                }
            }
            result.ElapsedMs = (int)watch.ElapsedMilliseconds;
            result.FailedTargets = Join(failed);

            if (result.SuccessCount == 0 && tcpFallback && !string.IsNullOrEmpty(tcpHost))
            {
                result.TcpFallbackUsed = true;
                ProbeSample tcp = TcpOnce(tcpHost, tcpPort, timeoutMs);
                result.TcpReachable = tcp.Success;
                result.TcpRtt = tcp.Rtt;
                result.TcpError = tcp.Error;
                if (tcp.Success)
                {
                    result.BestRtt = tcp.Rtt;
                    result.BestTarget = tcp.Target + " (TCP)";
                }
            }

            return result;
        }

        private static ProbeSample PingOnce(string target, int timeoutMs)
        {
            ProbeSample sample = new ProbeSample();
            sample.Target = target;
            try
            {
                using (Ping ping = new Ping())
                {
                    byte[] buffer = new byte[32];
                    PingOptions options = new PingOptions(128, false);
                    PingReply reply = ping.Send(target, timeoutMs, buffer, options);
                    if (reply != null && reply.Status == IPStatus.Success)
                    {
                        sample.Success = true;
                        sample.Rtt = (int)reply.RoundtripTime;
                    }
                    else
                    {
                        sample.Success = false;
                        sample.Rtt = -1;
                        sample.Error = reply == null ? "无响应" : reply.Status.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                sample.Success = false;
                sample.Rtt = -1;
                sample.Error = ex.Message;
            }
            return sample;
        }

        private static ProbeSample TcpOnce(string host, int port, int timeoutMs)
        {
            ProbeSample sample = new ProbeSample();
            sample.Target = host + ":" + port.ToString(CultureInfo.InvariantCulture);
            TcpClient client = new TcpClient();
            Stopwatch watch = Stopwatch.StartNew();
            try
            {
                IAsyncResult async = client.BeginConnect(host, port, null, null);
                if (async.AsyncWaitHandle.WaitOne(timeoutMs, false))
                {
                    try
                    {
                        client.EndConnect(async);
                        sample.Success = true;
                    }
                    catch (Exception ex)
                    {
                        sample.Success = false;
                        sample.Error = ex.Message;
                    }
                }
                else
                {
                    sample.Success = false;
                    sample.Error = "连接超时";
                }
            }
            catch (Exception ex)
            {
                sample.Success = false;
                sample.Error = ex.Message;
            }
            finally
            {
                watch.Stop();
                try { client.Close(); }
                catch (Exception) { }
            }

            sample.Rtt = sample.Success ? (int)watch.ElapsedMilliseconds : -1;
            return sample;
        }

        private static ProbeSample Failed(string target, string error)
        {
            ProbeSample sample = new ProbeSample();
            sample.Target = target;
            sample.Success = false;
            sample.Rtt = -1;
            sample.Error = error;
            return sample;
        }

        private static string Join(List<string> values)
        {
            if (values == null || values.Count == 0) { return ""; }
            string text = string.Join("、", values.ToArray());
            if (text.Length > 60) { text = text.Substring(0, 60) + "…"; }
            return text;
        }
    }
}
