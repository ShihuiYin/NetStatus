using System;
using System.Globalization;
using System.IO;
using System.Text;

namespace NetStatus
{
    /// <summary>
    /// 程序设置，保存在 %APPDATA%\NetStatus\settings.ini（INI 风格，可记事本直接改）。
    /// </summary>
    internal sealed class AppSettings
    {
        public const string DefaultTargets = "223.5.5.5, 119.29.29.29, 120.53.53.53, 180.76.76.76";

        // ---- 网络延时 ----
        public string[] Targets = SplitTargets(DefaultTargets);
        public int IntervalMs = 1500;             // 探测间隔
        public int TimeoutMs = 1000;              // 单次探测超时
        public int FailThreshold = 3;             // 连续失败多少次判定为断网/卡死
        public bool TcpFallback = true;           // ping 全部失败时改用 TCP 连接测试
        public string TcpHost = "www.baidu.com";  // TCP 兜底目标
        public int TcpPort = 443;
        public int FastMs = 80;                   // <= 绿色：很快
        public int SlowMs = 250;                  // <= 橙色：偏慢；再高就是红色
        public int LossOrangePercent = 25;        // 当轮丢包超过这个比例，至少显示橙色
        public int LossRedPercent = 60;           // 当轮丢包超过这个比例，显示红色

        // ---- CPU 指标 ----
        public bool ShowCpuIcon = true;                       // 是否显示第二个图标
        public CpuSensorKind CpuSensor = CpuSensorKind.Temperature;
        public int CpuIntervalMs = 2000;                      // CPU 采样间隔
        public int CpuLowC = 60;                              // 温度 <= 该值显示蓝色
        public int CpuHighC = 80;                             // 温度 >  该值显示红色
        public int CpuLowLoad = 40;                           // 占用率 <= 该值显示蓝色
        public int CpuHighLoad = 80;                          // 占用率 >  该值显示红色

        // ---- 界面 ----
        public bool ShowNumber = true;            // 图标上显示数值
        public bool ShowUnit = true;              // 数值后面显示单位（ms / °C / %）
        public bool NotifyOnStateChange = true;   // 状态变化时弹气泡提醒
        public int HistorySize = 60;              // 统计窗口（保留多少轮结果）

        public string FilePath = "";
        public bool IsFirstRun;

        public static string AppDataDir
        {
            get
            {
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "NetStatus");
            }
        }

        public static string DefaultPath
        {
            get { return Path.Combine(AppDataDir, "settings.ini"); }
        }

        public static AppSettings Load(string path)
        {
            AppSettings settings = new AppSettings();
            settings.FilePath = string.IsNullOrEmpty(path) ? DefaultPath : path;

            if (!File.Exists(settings.FilePath))
            {
                settings.IsFirstRun = true;
                settings.Save();
                return settings;
            }

            string[] lines;
            try { lines = File.ReadAllLines(settings.FilePath, Encoding.UTF8); }
            catch (Exception) { return settings; }

            for (int i = 0; i < lines.Length; i++)
            {
                string line = lines[i].Trim();
                if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[') { continue; }

                int index = line.IndexOf('=');
                if (index <= 0) { continue; }

                string key = line.Substring(0, index).Trim().ToLowerInvariant();
                string value = StripComment(line.Substring(index + 1).Trim());

                switch (key)
                {
                    case "targets": settings.Targets = SplitTargets(value); break;
                    case "intervalms": settings.IntervalMs = ParseInt(value, settings.IntervalMs); break;
                    case "timeoutms": settings.TimeoutMs = ParseInt(value, settings.TimeoutMs); break;
                    case "failthreshold": settings.FailThreshold = ParseInt(value, settings.FailThreshold); break;
                    case "tcpfallback": settings.TcpFallback = ParseBool(value, settings.TcpFallback); break;
                    case "tcphost": if (value.Length > 0) { settings.TcpHost = value; } break;
                    case "tcpport": settings.TcpPort = ParseInt(value, settings.TcpPort); break;
                    case "fastms": settings.FastMs = ParseInt(value, settings.FastMs); break;
                    case "slowms": settings.SlowMs = ParseInt(value, settings.SlowMs); break;
                    case "lossorangepercent": settings.LossOrangePercent = ParseInt(value, settings.LossOrangePercent); break;
                    case "lossredpercent": settings.LossRedPercent = ParseInt(value, settings.LossRedPercent); break;
                    case "showcpuicon": settings.ShowCpuIcon = ParseBool(value, settings.ShowCpuIcon); break;
                    case "cpusensor": settings.CpuSensor = ParseCpuSensor(value, settings.CpuSensor); break;
                    case "cpuintervalms": settings.CpuIntervalMs = ParseInt(value, settings.CpuIntervalMs); break;
                    case "cpulowc": settings.CpuLowC = ParseInt(value, settings.CpuLowC); break;
                    case "cpuhighc": settings.CpuHighC = ParseInt(value, settings.CpuHighC); break;
                    case "cpulowload": settings.CpuLowLoad = ParseInt(value, settings.CpuLowLoad); break;
                    case "cpuhighload": settings.CpuHighLoad = ParseInt(value, settings.CpuHighLoad); break;
                    case "shownumber": settings.ShowNumber = ParseBool(value, settings.ShowNumber); break;
                    case "showunit": settings.ShowUnit = ParseBool(value, settings.ShowUnit); break;
                    case "notifyonstatechange": settings.NotifyOnStateChange = ParseBool(value, settings.NotifyOnStateChange); break;
                    case "historysize": settings.HistorySize = ParseInt(value, settings.HistorySize); break;
                }
            }

            settings.Validate();
            return settings;
        }

        public void Save()
        {
            try
            {
                string dir = Path.GetDirectoryName(FilePath);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir)) { Directory.CreateDirectory(dir); }
                File.WriteAllText(FilePath, BuildContent(), new UTF8Encoding(true));
            }
            catch (Exception)
            {
                // 设置写不进去不影响程序运行
            }
        }

        public void Validate()
        {
            if (Targets == null || Targets.Length == 0) { Targets = SplitTargets(DefaultTargets); }
            IntervalMs = Clamp(IntervalMs, 300, 60000);
            TimeoutMs = Clamp(TimeoutMs, 200, 10000);
            FastMs = Clamp(FastMs, 1, 60000);
            SlowMs = Clamp(SlowMs, FastMs + 1, 120000);
            FailThreshold = Clamp(FailThreshold, 1, 100);
            LossOrangePercent = Clamp(LossOrangePercent, 0, 100);
            LossRedPercent = Clamp(LossRedPercent, LossOrangePercent + 1, 100);
            HistorySize = Clamp(HistorySize, 5, 1000);
            TcpPort = Clamp(TcpPort, 1, 65535);
            if (string.IsNullOrEmpty(TcpHost)) { TcpHost = "www.baidu.com"; }

            CpuIntervalMs = Clamp(CpuIntervalMs, 500, 60000);
            CpuLowC = Clamp(CpuLowC, 0, 120);
            CpuHighC = Clamp(CpuHighC, CpuLowC + 1, 150);
            CpuLowLoad = Clamp(CpuLowLoad, 0, 99);
            CpuHighLoad = Clamp(CpuHighLoad, CpuLowLoad + 1, 100);

            if (string.IsNullOrEmpty(FilePath)) { FilePath = DefaultPath; }
        }

        private string BuildContent()
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("# NetStatus 网络延时监测 - 设置文件");
            sb.AppendLine("# 改完保存后，在托盘图标上点右键 -> 重新载入设置（或重启程序）即可生效。");
            sb.AppendLine("# 行内注释请用分号 ; 开头。");
            sb.AppendLine();
            sb.AppendLine("# ---------- 网络延时 ----------");
            sb.AppendLine("# 探测目标，用逗号分隔。可填 IP 或域名。");
            sb.AppendLine("# 建议放 2~4 个稳定的国内目标，取其中最好的一次作为“当前延时”。");
            sb.AppendLine("# 注意：被网络屏蔽的目标每轮都会白等一个超时，请填自己能 ping 通的地址。");
            sb.AppendLine("Targets = " + string.Join(", ", Targets));
            sb.AppendLine();
            sb.AppendLine("# 探测间隔（毫秒）");
            sb.AppendLine("IntervalMs = " + Text(IntervalMs));
            sb.AppendLine();
            sb.AppendLine("# 单次探测超时（毫秒）");
            sb.AppendLine("TimeoutMs = " + Text(TimeoutMs));
            sb.AppendLine();
            sb.AppendLine("# 连续多少次完全没有响应后，判定为“卡顿 / 无法连接”");
            sb.AppendLine("FailThreshold = " + Text(FailThreshold));
            sb.AppendLine();
            sb.AppendLine("# 所有 ping 都失败时，是否改用 TCP 连接测试来判断网络是否真的不通");
            sb.AppendLine("# （有些网络会屏蔽 ICMP，但 TCP 可以通，打开它可以避免误报）");
            sb.AppendLine("TcpFallback = " + Text(TcpFallback));
            sb.AppendLine("TcpHost = " + TcpHost);
            sb.AppendLine("TcpPort = " + Text(TcpPort));
            sb.AppendLine();
            sb.AppendLine("# 延时 <= FastMs         -> 绿色（很快）");
            sb.AppendLine("# 延时 <= SlowMs         -> 橙色（偏慢）");
            sb.AppendLine("# 再高，或者丢包太严重   -> 红色（卡顿 / 无法连接）");
            sb.AppendLine("FastMs = " + Text(FastMs));
            sb.AppendLine("SlowMs = " + Text(SlowMs));
            sb.AppendLine();
            sb.AppendLine("# 当轮丢包率超过 LossOrangePercent 时至少显示橙色；超过 LossRedPercent 显示红色");
            sb.AppendLine("LossOrangePercent = " + Text(LossOrangePercent));
            sb.AppendLine("LossRedPercent = " + Text(LossRedPercent));
            sb.AppendLine();
            sb.AppendLine("# ---------- CPU 指标（第二个图标）----------");
            sb.AppendLine("# 是否显示 CPU 图标");
            sb.AppendLine("ShowCpuIcon = " + Text(ShowCpuIcon));
            sb.AppendLine();
            sb.AppendLine("# CPU 指标：temperature = 温度（蓝/橙/红），load = 使用率（蓝/橙/红），off = 不采样");
            sb.AppendLine("CpuSensor = " + CpuSensorText(CpuSensor));
            sb.AppendLine();
            sb.AppendLine("# CPU 采样间隔（毫秒），温度读取较慢，不建议低于 1000");
            sb.AppendLine("CpuIntervalMs = " + Text(CpuIntervalMs));
            sb.AppendLine();
            sb.AppendLine("# 温度 <= CpuLowC 显示蓝色，温度 > CpuHighC 显示红色，中间显示橙色");
            sb.AppendLine("CpuLowC = " + Text(CpuLowC));
            sb.AppendLine("CpuHighC = " + Text(CpuHighC));
            sb.AppendLine();
            sb.AppendLine("# 使用率 <= CpuLowLoad 显示蓝色，使用率 > CpuHighLoad 显示红色，中间显示橙色");
            sb.AppendLine("CpuLowLoad = " + Text(CpuLowLoad));
            sb.AppendLine("CpuHighLoad = " + Text(CpuHighLoad));
            sb.AppendLine();
            sb.AppendLine("# ---------- 界面 ----------");
            sb.AppendLine("# 是否在图标上显示数值（关掉则只显示一个颜色圆点）");
            sb.AppendLine("ShowNumber = " + Text(ShowNumber));
            sb.AppendLine();
            sb.AppendLine("# 数值下面是否显示单位（ms / °C / %）");
            sb.AppendLine("ShowUnit = " + Text(ShowUnit));
            sb.AppendLine();
            sb.AppendLine("# 网络状态变差 / 恢复时是否弹出气泡提醒");
            sb.AppendLine("NotifyOnStateChange = " + Text(NotifyOnStateChange));
            sb.AppendLine();
            sb.AppendLine("# 统计窗口保留多少轮结果，用于计算平均 / 最大 / 丢包率");
            sb.AppendLine("HistorySize = " + Text(HistorySize));
            return sb.ToString();
        }

        private static string CpuSensorText(CpuSensorKind kind)
        {
            switch (kind)
            {
                case CpuSensorKind.Load: return "load";
                case CpuSensorKind.Off: return "off";
                default: return "temperature";
            }
        }

        private static string Text(int value)
        {
            return value.ToString(CultureInfo.InvariantCulture);
        }

        private static string Text(bool value)
        {
            return value ? "true" : "false";
        }

        private static string StripComment(string value)
        {
            int cut = value.IndexOf(';');
            int hash = value.IndexOf('#');
            if (hash >= 0 && (cut < 0 || hash < cut)) { cut = hash; }
            if (cut >= 0) { value = value.Substring(0, cut); }
            return value.Trim();
        }

        private static int ParseInt(string value, int fallback)
        {
            int parsed;
            if (int.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out parsed)) { return parsed; }
            return fallback;
        }

        private static bool ParseBool(string value, bool fallback)
        {
            if (value == null) { return fallback; }
            string text = value.Trim().ToLowerInvariant();
            if (text.Length == 0) { return fallback; }
            if (text == "1" || text == "true" || text == "yes" || text == "on" || text == "是" || text == "开") { return true; }
            if (text == "0" || text == "false" || text == "no" || text == "off" || text == "否" || text == "关") { return false; }
            return fallback;
        }

        private static CpuSensorKind ParseCpuSensor(string value, CpuSensorKind fallback)
        {
            if (value == null) { return fallback; }
            string text = value.Trim().ToLowerInvariant();
            if (text == "temperature" || text == "temp" || text == "温度") { return CpuSensorKind.Temperature; }
            if (text == "load" || text == "usage" || text == "使用率" || text == "占用率") { return CpuSensorKind.Load; }
            if (text == "off" || text == "none" || text == "关闭") { return CpuSensorKind.Off; }
            return fallback;
        }

        private static int Clamp(int value, int min, int max)
        {
            if (value < min) { return min; }
            if (value > max) { return max; }
            return value;
        }

        internal static string[] SplitTargets(string text)
        {
            if (string.IsNullOrEmpty(text)) { return new string[0]; }
            char[] separators = new char[] { ',', ';', ' ', '\t', '|' };
            string[] parts = text.Split(separators, StringSplitOptions.RemoveEmptyEntries);
            int count = 0;
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i].Trim();
                if (part.Length > 0) { parts[count] = part; count++; }
            }
            string[] result = new string[count];
            Array.Copy(parts, result, count);
            return result;
        }
    }
}
