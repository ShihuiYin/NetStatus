using System;
using System.Globalization;
using System.Text;
using System.Management;
using System.Runtime.InteropServices;
using System.Threading;

namespace NetStatus
{
    internal enum CpuMetric
    {
        Temperature = 0,
        Load = 1
    }

    internal enum CpuSensorKind
    {
        Temperature = 0,
        Load = 1,
        Off = 2
    }

    internal sealed class CpuReading
    {
        public bool Available;
        public CpuMetric Metric = CpuMetric.Temperature;
        public double Value;
        public string Source = "";
        public string Detail = "";
        public string Error = "";
    }

    /// <summary>
    /// 读取 CPU 温度 / 占用率。
    ///
    /// 温度没有统一的标准接口，只能按可靠性依次尝试：
    ///   1. LibreHardwareMonitor / OpenHardwareMonitor 的 WMI（桌面平台基本只有这种能读到，需要先装并运行它）
    ///   2. ACPI 热区 MSAcpi_ThermalZoneTemperature（多数笔记本支持，部分机器需要管理员权限）
    ///   3. Win32_PerfFormattedData_Counters_ThermalZoneInformation（少数机器支持）
    /// 都读不到时返回 Available = false，由界面提示用户，而不是显示假数据。
    ///
    /// 占用率则用 GetSystemTimes，不依赖任何外部组件，永远可用。
    /// </summary>
    internal static class CpuSensor
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool GetSystemTimes(out long idleTime, out long kernelTime, out long userTime);

        private static long _prevIdle;
        private static long _prevKernel;
        private static long _prevUser;
        private static bool _hasSample;

        // ------------------------------------------------------------ 占用率

        public static CpuReading ReadLoad()
        {
            CpuReading reading = new CpuReading();
            reading.Metric = CpuMetric.Load;
            reading.Source = "系统计数器";
            reading.Detail = "CPU 总占用率";

            double load = ReadLoadPercent();
            if (load < 0)
            {
                reading.Error = "正在采样…";
                return reading;
            }

            reading.Available = true;
            reading.Value = load;
            return reading;
        }

        private static double ReadLoadPercent()
        {
            long idle, kernel, user;
            if (!GetSystemTimes(out idle, out kernel, out user)) { return -1; }

            if (!_hasSample)
            {
                _prevIdle = idle;
                _prevKernel = kernel;
                _prevUser = user;
                _hasSample = true;
                Thread.Sleep(150);
                if (!GetSystemTimes(out idle, out kernel, out user)) { return -1; }
            }

            long idleDelta = idle - _prevIdle;
            long totalDelta = (kernel - _prevKernel) + (user - _prevUser);
            _prevIdle = idle;
            _prevKernel = kernel;
            _prevUser = user;

            if (totalDelta <= 0) { return -1; }

            double load = (double)(totalDelta - idleDelta) * 100.0 / totalDelta;
            if (load < 0) { load = 0; }
            if (load > 100) { load = 100; }
            return load;
        }

        // ------------------------------------------------------------ 温度

        public static CpuReading ReadTemperature()
        {
            CpuReading reading = new CpuReading();
            reading.Metric = CpuMetric.Temperature;

            StringBuilder log = new StringBuilder();
            double value;
            string detail;
            string error;

            if (TryMonitorProvider("root\\LibreHardwareMonitor", "LibreHardwareMonitor", out value, out detail, out error))
            {
                return Success(reading, value, "LibreHardwareMonitor", detail);
            }
            Append(log, error);

            if (TryMonitorProvider("root\\OpenHardwareMonitor", "OpenHardwareMonitor", out value, out detail, out error))
            {
                return Success(reading, value, "OpenHardwareMonitor", detail);
            }
            Append(log, error);

            if (TryAcpiThermalZone(out value, out detail, out error))
            {
                return Success(reading, value, "ACPI 热区", detail);
            }
            Append(log, error);

            if (TryThermalZoneCounter(out value, out detail, out error))
            {
                return Success(reading, value, "性能计数器", detail);
            }
            Append(log, error);

            reading.Available = false;
            reading.Error = "本机未提供 CPU 温度（可改用“使用率”）";
            reading.Detail = log.ToString();
            return reading;
        }

        private static CpuReading Success(CpuReading reading, double value, string source, string detail)
        {
            reading.Available = true;
            reading.Value = value;
            reading.Source = source;
            reading.Detail = detail;
            return reading;
        }

        private static void Append(StringBuilder log, string error)
        {
            if (string.IsNullOrEmpty(error)) { return; }
            if (log.Length > 0) { log.Append("；"); }
            log.Append(error);
        }

        /// <summary>读 LibreHardwareMonitor / OpenHardwareMonitor 暴露的 WMI 传感器。</summary>
        private static bool TryMonitorProvider(string ns, string label, out double value, out string detail, out string error)
        {
            value = 0;
            detail = "";
            error = "";

            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    ns, "SELECT Name, Identifier, SensorType, Value FROM Sensor"))
                using (ManagementObjectCollection items = searcher.Get())
                {
                    int bestScore = -1;
                    double bestValue = 0;
                    string bestName = "";

                    foreach (ManagementBaseObject item in items)
                    {
                        using (item)
                        {
                            string type = Text(item["SensorType"]);
                            if (type != "Temperature") { continue; }

                            double temperature = Number(item["Value"]);
                            if (temperature <= 0 || temperature > 150) { continue; }

                            string name = Text(item["Name"]);
                            string identifier = Text(item["Identifier"]);
                            int score = ScoreTemperatureSensor(name, identifier);
                            if (score > bestScore)
                            {
                                bestScore = score;
                                bestValue = temperature;
                                bestName = name;
                            }
                        }
                    }

                    if (bestScore >= 0)
                    {
                        value = bestValue;
                        detail = bestName;
                        return true;
                    }

                    error = label + " 里没有找到 CPU 温度传感器";
                    return false;
                }
            }
            catch (ManagementException ex)
            {
                error = label + " 未运行：" + ex.Message;
                return false;
            }
            catch (Exception ex)
            {
                error = label + " 读取失败：" + ex.Message;
                return false;
            }
        }

        private static int ScoreTemperatureSensor(string name, string identifier)
        {
            string n = (name ?? "").ToLowerInvariant();
            string id = (identifier ?? "").ToLowerInvariant();

            // 先排除明显不是 CPU 的传感器
            string[] notCpu = new string[] { "gpu", "hdd", "ssd", "drive", "ram", "memory", "battery", "psu", "mainboard", "motherboard", "vrm", "cooler", "fan", "nic", "network" };
            for (int i = 0; i < notCpu.Length; i++)
            {
                if (n.IndexOf(notCpu[i], StringComparison.Ordinal) >= 0) { return -1; }
                if (id.IndexOf("/" + notCpu[i], StringComparison.Ordinal) >= 0) { return -1; }
            }

            if (n.IndexOf("tctl", StringComparison.Ordinal) >= 0) { return 100; }
            if (n.IndexOf("tdie", StringComparison.Ordinal) >= 0) { return 98; }
            if (n.IndexOf("cpu package", StringComparison.Ordinal) >= 0) { return 96; }
            if (n.IndexOf("package", StringComparison.Ordinal) >= 0) { return 92; }
            if (n.IndexOf("core max", StringComparison.Ordinal) >= 0) { return 90; }
            if (n.IndexOf("core average", StringComparison.Ordinal) >= 0) { return 88; }
            if (n.IndexOf("cpu", StringComparison.Ordinal) >= 0) { return 80; }
            if (id.StartsWith("/amdcpu", StringComparison.Ordinal) || id.StartsWith("/intelcpu", StringComparison.Ordinal)) { return 70; }
            if (n.IndexOf("core", StringComparison.Ordinal) >= 0) { return 50; }
            return 10;
        }

        /// <summary>ACPI 热区，单位是绝对温度（十分之一开尔文）。</summary>
        private static bool TryAcpiThermalZone(out double value, out string detail, out string error)
        {
            value = 0;
            detail = "";
            error = "";

            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "root\\WMI", "SELECT InstanceName, CurrentTemperature FROM MSAcpi_ThermalZoneTemperature"))
                using (ManagementObjectCollection items = searcher.Get())
                {
                    double best = double.MinValue;
                    string bestName = "";

                    foreach (ManagementBaseObject item in items)
                    {
                        using (item)
                        {
                            double celsius = Number(item["CurrentTemperature"]) / 10.0 - 273.15;
                            if (celsius < -20 || celsius > 150) { continue; }
                            if (celsius > best)
                            {
                                best = celsius;
                                bestName = Text(item["InstanceName"]);
                            }
                        }
                    }

                    if (best > double.MinValue)
                    {
                        value = best;
                        detail = "热区 " + (bestName.Length == 0 ? "?" : bestName);
                        return true;
                    }

                    error = "本机没有可用的 ACPI 热区";
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = "ACPI 热区不可用：" + ex.Message;
                return false;
            }
        }

        /// <summary>少数机器会把这个计数器暴露出来，Temperature 单位是开尔文。</summary>
        private static bool TryThermalZoneCounter(out double value, out string detail, out string error)
        {
            value = 0;
            detail = "";
            error = "";

            try
            {
                using (ManagementObjectSearcher searcher = new ManagementObjectSearcher(
                    "root\\CIMv2",
                    "SELECT Name, Temperature, HighPrecisionTemperature FROM Win32_PerfFormattedData_Counters_ThermalZoneInformation"))
                using (ManagementObjectCollection items = searcher.Get())
                {
                    double best = double.MinValue;
                    string bestName = "";

                    foreach (ManagementBaseObject item in items)
                    {
                        using (item)
                        {
                            double high = Number(item["HighPrecisionTemperature"]);
                            double celsius = high > 0
                                ? high / 10.0 - 273.15
                                : Number(item["Temperature"]) - 273.15;
                            if (celsius < -20 || celsius > 150) { continue; }
                            if (celsius > best)
                            {
                                best = celsius;
                                bestName = Text(item["Name"]);
                            }
                        }
                    }

                    if (best > double.MinValue)
                    {
                        value = best;
                        detail = "热区 " + (bestName.Length == 0 ? "?" : bestName);
                        return true;
                    }

                    error = "本机没有暴露热区性能计数器（CPU 温度通常需要硬件监控工具支持）";
                    return false;
                }
            }
            catch (Exception ex)
            {
                error = "热区计数器不可用：" + ex.Message;
                return false;
            }
        }

        private static string Text(object value)
        {
            return value == null ? "" : Convert.ToString(value, CultureInfo.InvariantCulture);
        }

        private static double Number(object value)
        {
            if (value == null) { return 0; }
            try { return Convert.ToDouble(value, CultureInfo.InvariantCulture); }
            catch (Exception) { return 0; }
        }
    }
}
