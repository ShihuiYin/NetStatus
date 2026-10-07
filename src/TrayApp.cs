using System;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Windows.Forms;
using Microsoft.Win32;

namespace NetStatus
{
    /// <summary>任务栏托盘小程序：网络延时 + CPU 指标，都用颜色和数字直接显示在托盘图标上。</summary>
    internal sealed class TrayApp : ApplicationContext
    {
        private const string RunKeyPath = @"Software\Microsoft\Windows\CurrentVersion\Run";
        private const string RunValueName = "NetStatus";

        private readonly LatencyMonitor _monitor;
        private readonly CpuMonitor _cpuMonitor;
        private readonly TrayIconSlot _slotNetwork = new TrayIconSlot();
        private readonly TrayIconSlot _slotCpu = new TrayIconSlot();
        private readonly Control _dispatcher = new Control();
        private readonly ContextMenuStrip _menu = new ContextMenuStrip();

        private readonly ToolStripMenuItem _statusItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _detailItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _cpuItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _pauseItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _numberItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _unitItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _notifyItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _cpuShowItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _cpuTempItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _cpuLoadItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _cpuOffItem = new ToolStripMenuItem();
        private readonly ToolStripMenuItem _startupItem = new ToolStripMenuItem();

        private AppSettings _settings;
        private LatencyLevel _lastNotifiedLevel = LatencyLevel.Unknown;
        private bool _paused;
        private bool _exiting;

        public TrayApp(AppSettings settings)
        {
            _settings = settings;

            GC.KeepAlive(_dispatcher.Handle);

            BuildMenu();

            _monitor = new LatencyMonitor(_settings);
            _monitor.Updated += new EventHandler<MonitorUpdate>(OnNetworkUpdated);

            _cpuMonitor = new CpuMonitor(_settings);
            _cpuMonitor.Updated += new EventHandler<CpuReading>(OnCpuUpdated);

            _slotNetwork.Attach(_menu, new MouseEventHandler(OnTrayMouseUp), new EventHandler(OnTrayDoubleClick));
            _slotCpu.Attach(_menu, new MouseEventHandler(OnTrayMouseUp), new EventHandler(OnTrayDoubleClick));

            ApplyNetwork(null);
            ApplyCpu(null);
            _slotNetwork.Show(true);
            _slotCpu.Show(_settings.ShowCpuIcon && _settings.CpuSensor != CpuSensorKind.Off);

            _monitor.Start();
            _cpuMonitor.Start();

            if (_settings.IsFirstRun)
            {
                _slotNetwork.ShowBalloon(8000, "网络延时监测已启动",
                    "第一个图标是网络延时（绿色很快 / 橙色偏慢 / 红色卡顿或断网），第二个是 CPU 指标。右键图标可查看详情和设置。",
                    ToolTipIcon.Info);
            }
        }

        // ---------------------------------------------------------------- 菜单

        private void BuildMenu()
        {
            _statusItem.Enabled = false;
            _detailItem.Enabled = false;
            _cpuItem.Enabled = false;
            _statusItem.Text = "正在检测…";
            _detailItem.Text = " ";
            _cpuItem.Text = "CPU 指标：正在读取…";

            _pauseItem.Text = "暂停监测(&P)";
            _pauseItem.Click += new EventHandler(OnTogglePause);

            _numberItem.Text = "在图标上显示数值(&N)";
            _numberItem.Checked = _settings.ShowNumber;
            _numberItem.Click += new EventHandler(OnToggleNumber);

            _unitItem.Text = "显示单位（ms / °C / %）(&U)";
            _unitItem.Checked = _settings.ShowUnit;
            _unitItem.Click += new EventHandler(OnToggleUnit);

            _notifyItem.Text = "状态变化时通知我(&M)";
            _notifyItem.Checked = _settings.NotifyOnStateChange;
            _notifyItem.Click += new EventHandler(OnToggleNotify);

            _cpuShowItem.Text = "显示 CPU 图标(&C)";
            _cpuShowItem.Checked = _settings.ShowCpuIcon;
            _cpuShowItem.Click += new EventHandler(OnToggleCpuIcon);

            _cpuTempItem.Text = "温度(&T)";
            _cpuTempItem.Click += new EventHandler(OnCpuTemperature);
            _cpuLoadItem.Text = "使用率(&L)";
            _cpuLoadItem.Click += new EventHandler(OnCpuLoad);
            _cpuOffItem.Text = "关闭采样(&F)";
            _cpuOffItem.Click += new EventHandler(OnCpuOff);

            ToolStripMenuItem cpuMenu = new ToolStripMenuItem("CPU 指标(&K)");
            cpuMenu.DropDownItems.Add(_cpuTempItem);
            cpuMenu.DropDownItems.Add(_cpuLoadItem);
            cpuMenu.DropDownItems.Add(_cpuOffItem);
            cpuMenu.DropDownItems.Add(new ToolStripSeparator());
            cpuMenu.DropDownItems.Add(CreateItem("为什么读不到 CPU 温度？(&H)", new EventHandler(OnCpuHelp)));
            UpdateCpuMenuChecks();

            _startupItem.Text = "开机自动启动(&A)";
            _startupItem.Checked = IsStartupEnabled();
            _startupItem.Click += new EventHandler(OnToggleStartup);

            _menu.Items.Add(_statusItem);
            _menu.Items.Add(_detailItem);
            _menu.Items.Add(_cpuItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(CreateItem("立即检测(&T)", new EventHandler(OnProbeNow)));
            _menu.Items.Add(_pauseItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_numberItem);
            _menu.Items.Add(_unitItem);
            _menu.Items.Add(_notifyItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(_cpuShowItem);
            _menu.Items.Add(cpuMenu);
            _menu.Items.Add(_startupItem);
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(CreateItem("打开设置文件(&S)", new EventHandler(OnOpenSettings)));
            _menu.Items.Add(CreateItem("重新载入设置(&R)", new EventHandler(OnReloadSettings)));
            _menu.Items.Add(CreateItem("打开数据目录(&D)", new EventHandler(OnOpenDataFolder)));
            _menu.Items.Add(CreateItem("关于(&B)", new EventHandler(OnAbout)));
            _menu.Items.Add(new ToolStripSeparator());
            _menu.Items.Add(CreateItem("退出(&X)", new EventHandler(OnExit)));
        }

        private static ToolStripMenuItem CreateItem(string text, EventHandler handler)
        {
            ToolStripMenuItem item = new ToolStripMenuItem(text);
            item.Click += handler;
            return item;
        }

        private void UpdateCpuMenuChecks()
        {
            _cpuTempItem.Checked = _settings.CpuSensor == CpuSensorKind.Temperature;
            _cpuLoadItem.Checked = _settings.CpuSensor == CpuSensorKind.Load;
            _cpuOffItem.Checked = _settings.CpuSensor == CpuSensorKind.Off;
            _cpuTempItem.Enabled = _settings.ShowCpuIcon;
            _cpuLoadItem.Enabled = _settings.ShowCpuIcon;
            _cpuOffItem.Enabled = _settings.ShowCpuIcon;
        }

        // ---------------------------------------------------------------- 后台更新

        private void OnNetworkUpdated(object sender, MonitorUpdate update)
        {
            if (_exiting) { return; }
            MonitorUpdate captured = update;
            Post(delegate { ApplyNetwork(captured); });
        }

        private void OnCpuUpdated(object sender, CpuReading reading)
        {
            if (_exiting) { return; }
            CpuReading captured = reading;
            Post(delegate { ApplyCpu(captured); });
        }

        private void Post(Action action)
        {
            try { _dispatcher.BeginInvoke(action); }
            catch (Exception) { }
        }

        // ---------------------------------------------------------------- 网络图标

        private void ApplyNetwork(MonitorUpdate update)
        {
            MonitorUpdate current = update == null ? _monitor.Current : update;
            if (current == null) { current = new MonitorUpdate(); }

            string text = _settings.ShowNumber ? BuildNetworkText(current) : null;
            _slotNetwork.SetIcon(LatencyLevelInfo.ColorOf(current.Level), text, _settings.ShowUnit ? "ms" : null);
            _slotNetwork.SetText(BuildNetworkTooltip(current));

            _statusItem.Text = BuildStatusLine(current);
            _detailItem.Text = BuildDetailLine(current);
            NotifyIfNeeded(current);
        }

        private static string BuildNetworkText(MonitorUpdate update)
        {
            if (update.Last == null || update.Level == LatencyLevel.Unknown) { return ""; }
            if (update.Offline) { return "×"; }
            if (!update.Last.HasResponse) { return "--"; }

            int rtt = update.Last.BestRtt;
            if (rtt < 1000) { return rtt.ToString(CultureInfo.InvariantCulture); }
            return (rtt / 1000).ToString(CultureInfo.InvariantCulture) + "s";
        }

        private string BuildNetworkTooltip(MonitorUpdate update)
        {
            if (_paused) { return "已暂停监测"; }
            if (update.Last == null) { return "网络延时监测启动中…"; }

            string latency;
            if (update.Offline) { latency = "无法连接"; }
            else if (!update.Last.HasResponse) { latency = "无响应"; }
            else { latency = update.Last.BestRtt.ToString(CultureInfo.InvariantCulture) + " ms"; }

            string text = string.Format(CultureInfo.InvariantCulture, "{0}｜延时 {1}｜丢包 {2}%",
                LatencyLevelInfo.TextOf(update.Level), latency, update.LossPercent);
            return Truncate(text, 62);
        }

        private static string BuildStatusLine(MonitorUpdate update)
        {
            if (update.Last == null) { return "正在检测…"; }
            if (!update.Last.NetworkAvailable) { return "未检测到可用网络"; }
            if (update.Offline) { return "无法连接（连续 " + update.ConsecutiveFailures + " 次无响应）"; }
            if (!update.Last.HasResponse) { return "本轮无响应（第 " + update.ConsecutiveFailures + " 次）"; }

            return string.Format(CultureInfo.InvariantCulture, "延时 {0} ms（{1}）",
                update.Last.BestRtt, LatencyLevelInfo.TextOf(update.Level));
        }

        private static string BuildDetailLine(MonitorUpdate update)
        {
            if (update.Last == null) { return " "; }

            string target = string.IsNullOrEmpty(update.Last.BestTarget) ? "—" : update.Last.BestTarget;
            string average = update.AverageRtt >= 0
                ? update.AverageRtt.ToString(CultureInfo.InvariantCulture) + " ms" : "—";
            string range = update.MinRtt >= 0
                ? update.MinRtt.ToString(CultureInfo.InvariantCulture) + "~" +
                  update.MaxRtt.ToString(CultureInfo.InvariantCulture) + " ms"
                : "—";

            string text = string.Format(CultureInfo.InvariantCulture,
                "{0} ｜ 丢包 {1}% ｜ 平均 {2} ｜ 区间 {3}", target, update.LossPercent, average, range);

            if (update.Last.SuccessCount == 0 && update.Last.FailedTargets.Length > 0)
            {
                text = text + " ｜ 失败 " + update.Last.FailedTargets;
            }
            return text;
        }

        private void NotifyIfNeeded(MonitorUpdate update)
        {
            LatencyLevel previous = _lastNotifiedLevel;
            _lastNotifiedLevel = update.Level;

            if (!_settings.NotifyOnStateChange) { return; }
            if (previous == LatencyLevel.Unknown || previous == update.Level) { return; }

            if (update.Level == LatencyLevel.Bad)
            {
                string reason = update.Last != null && !update.Last.NetworkAvailable
                    ? "系统未检测到可用网络，请检查网线 / Wi-Fi 是否连接。"
                    : "多次探测都没有响应，网络可能已经中断。";
                _slotNetwork.ShowBalloon(6000, "网络卡顿或已断开", reason, ToolTipIcon.Warning);
            }
            else if (update.Level == LatencyLevel.Fast && previous == LatencyLevel.Bad)
            {
                _slotNetwork.ShowBalloon(4000, "网络已恢复", "延时已恢复正常。", ToolTipIcon.Info);
            }
        }

        // ---------------------------------------------------------------- CPU 图标

        private void ApplyCpu(CpuReading reading)
        {
            CpuReading current = reading == null ? _cpuMonitor.Current : reading;
            if (current == null) { current = new CpuReading(); }

            bool show = _settings.ShowCpuIcon && _settings.CpuSensor != CpuSensorKind.Off;
            _slotCpu.Show(show);

            if (!show)
            {
                _cpuItem.Text = _settings.ShowCpuIcon ? "CPU 指标：已关闭采样" : "CPU 图标：已隐藏";
                return;
            }

            MetricLevel level = CpuLevelOf(current);
            string text, unit, status;
            BuildCpuDisplay(current, level, out text, out unit, out status);

            _slotCpu.SetIcon(MetricLevelInfo.ColorOf(level), _settings.ShowNumber ? text : null,
                _settings.ShowUnit ? unit : null);
            _slotCpu.SetText(BuildCpuTooltip(current, level, text, unit));
            _cpuItem.Text = status;
        }

        private MetricLevel CpuLevelOf(CpuReading reading)
        {
            if (reading == null || !reading.Available) { return MetricLevel.Unknown; }

            if (reading.Metric == CpuMetric.Load)
            {
                if (reading.Value <= _settings.CpuLowLoad) { return MetricLevel.Low; }
                if (reading.Value <= _settings.CpuHighLoad) { return MetricLevel.Mid; }
                return MetricLevel.High;
            }

            if (reading.Value <= _settings.CpuLowC) { return MetricLevel.Low; }
            if (reading.Value <= _settings.CpuHighC) { return MetricLevel.Mid; }
            return MetricLevel.High;
        }

        private void BuildCpuDisplay(CpuReading reading, MetricLevel level, out string text, out string unit, out string status)
        {
            bool load = _settings.CpuSensor == CpuSensorKind.Load;
            string metric = load ? "CPU 占用" : "CPU 温度";
            unit = load ? "%" : "°C";

            if (!reading.Available)
            {
                text = "--";
                status = string.IsNullOrEmpty(reading.Error)
                    ? metric + "：正在读取…"
                    : metric + "：不可用（右键查看原因）";
                return;
            }

            text = ((int)Math.Round(reading.Value)).ToString(CultureInfo.InvariantCulture);
            status = string.Format(CultureInfo.InvariantCulture, "{0} {1} {2}（{3}）",
                metric, text, unit, MetricLevelInfo.TextOf(level));
        }

        private string BuildCpuTooltip(CpuReading reading, MetricLevel level, string text, string unit)
        {
            if (!reading.Available)
            {
                string reason = string.IsNullOrEmpty(reading.Error) ? "正在读取 CPU 传感器…" : reading.Error;
                return Truncate("CPU 指标不可用｜" + reason, 62);
            }

            string source = string.IsNullOrEmpty(reading.Source) ? "" : "｜" + reading.Source;
            string detail = string.IsNullOrEmpty(reading.Detail) ? "" : " · " + reading.Detail;
            return Truncate("CPU " + text + " " + unit + "（" + MetricLevelInfo.TextOf(level) + "）" + source + detail, 62);
        }

        // ---------------------------------------------------------------- 交互事件

        private void OnProbeNow(object sender, EventArgs e)
        {
            _monitor.RequestProbe();
            _cpuMonitor.RequestSample();
        }

        private void OnTrayMouseUp(object sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                try { _menu.Show(Cursor.Position); }
                catch (Exception) { }
            }
        }

        private void OnTrayDoubleClick(object sender, EventArgs e)
        {
            OnProbeNow(sender, EventArgs.Empty);
        }

        private void OnTogglePause(object sender, EventArgs e)
        {
            _paused = !_paused;
            _pauseItem.Checked = _paused;
            _pauseItem.Text = _paused ? "继续监测(&P)" : "暂停监测(&P)";
            _monitor.SetPaused(_paused);
            ApplyNetwork(null);
        }

        private void OnToggleNumber(object sender, EventArgs e)
        {
            _settings.ShowNumber = !_settings.ShowNumber;
            _numberItem.Checked = _settings.ShowNumber;
            _settings.Save();
            ApplyNetwork(null);
            ApplyCpu(null);
        }

        private void OnToggleUnit(object sender, EventArgs e)
        {
            _settings.ShowUnit = !_settings.ShowUnit;
            _unitItem.Checked = _settings.ShowUnit;
            _settings.Save();
            ApplyNetwork(null);
            ApplyCpu(null);
        }

        private void OnToggleNotify(object sender, EventArgs e)
        {
            _settings.NotifyOnStateChange = !_settings.NotifyOnStateChange;
            _notifyItem.Checked = _settings.NotifyOnStateChange;
            _settings.Save();
        }

        private void OnToggleCpuIcon(object sender, EventArgs e)
        {
            _settings.ShowCpuIcon = !_settings.ShowCpuIcon;
            _cpuShowItem.Checked = _settings.ShowCpuIcon;
            _settings.Save();
            UpdateCpuMenuChecks();
            ApplyCpu(null);
            _cpuMonitor.RequestSample();
        }

        private void OnCpuTemperature(object sender, EventArgs e)
        {
            SetCpuSensor(CpuSensorKind.Temperature);
        }

        private void OnCpuLoad(object sender, EventArgs e)
        {
            SetCpuSensor(CpuSensorKind.Load);
        }

        private void OnCpuOff(object sender, EventArgs e)
        {
            SetCpuSensor(CpuSensorKind.Off);
        }

        private void SetCpuSensor(CpuSensorKind kind)
        {
            _settings.CpuSensor = kind;
            if (kind != CpuSensorKind.Off) { _settings.ShowCpuIcon = true; }
            _settings.Save();
            _cpuShowItem.Checked = _settings.ShowCpuIcon;
            UpdateCpuMenuChecks();
            _cpuMonitor.ApplySettings(_settings);
            ApplyCpu(null);
        }

        private void OnCpuHelp(object sender, EventArgs e)
        {
            string text =
                "Windows 没有统一的 CPU 温度接口，能不能读到取决于硬件和系统：\n\n" +
                "1) 大多数笔记本：可以直接从 ACPI 热区读到，程序会自动尝试。\n\n" +
                "2) 很多台式机 / 迷你主机（例如 AMD Ryzen 平台）：系统根本不向外暴露温度，\n" +
                "   用户态程序无论如何都读不到，需要装硬件监控工具\n" +
                "   （LibreHardwareMonitor 或 OpenHardwareMonitor，建议以管理员运行），\n" +
                "   并在其中开启 WMI 支持；本程序会自动识别，不用额外配置。\n\n" +
                "3) 也可以把 CPU 指标改成“使用率”：它不依赖任何外部工具，\n" +
                "   蓝色=负载低，橙色=中等，红色=负载高。\n\n" +
                "当前状态：" + _cpuItem.Text;
            CpuReading current = _cpuMonitor.Current;
            if (current != null && !string.IsNullOrEmpty(current.Detail))
            {
                text = text + "\n\n—— 诊断信息 ——\n" + current.Detail;
            }
            MessageBox.Show(text, "CPU 温度说明", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnToggleStartup(object sender, EventArgs e)
        {
            bool enable = !IsStartupEnabled();
            try
            {
                SetStartup(enable);
            }
            catch (Exception ex)
            {
                MessageBox.Show("设置开机启动失败：" + ex.Message, "网络延时监测",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
            _startupItem.Checked = IsStartupEnabled();
        }

        private void OnOpenSettings(object sender, EventArgs e)
        {
            try
            {
                if (!File.Exists(_settings.FilePath)) { _settings.Save(); }
                Process.Start("notepad.exe", "\"" + _settings.FilePath + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开设置文件：" + ex.Message, "网络延时监测",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnReloadSettings(object sender, EventArgs e)
        {
            AppSettings reloaded = AppSettings.Load(_settings.FilePath);
            _settings = reloaded;

            _monitor.ApplySettings(reloaded);
            _cpuMonitor.ApplySettings(reloaded);

            _numberItem.Checked = reloaded.ShowNumber;
            _unitItem.Checked = reloaded.ShowUnit;
            _notifyItem.Checked = reloaded.NotifyOnStateChange;
            _cpuShowItem.Checked = reloaded.ShowCpuIcon;
            UpdateCpuMenuChecks();

            ApplyNetwork(null);
            ApplyCpu(null);

            MessageBox.Show(
                "设置已重新载入。\n\n探测目标：" + string.Join("、", reloaded.Targets) +
                "\n探测间隔：" + reloaded.IntervalMs + " ms" +
                "\n绿色上限：" + reloaded.FastMs + " ms" +
                "\n橙色上限：" + reloaded.SlowMs + " ms" +
                "\nCPU 指标：" + (_settings.CpuSensor == CpuSensorKind.Load ? "使用率" :
                                  _settings.CpuSensor == CpuSensorKind.Off ? "关闭" : "温度"),
                "网络延时监测", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnOpenDataFolder(object sender, EventArgs e)
        {
            try
            {
                Directory.CreateDirectory(AppSettings.AppDataDir);
                Process.Start("explorer.exe", "\"" + AppSettings.AppDataDir + "\"");
            }
            catch (Exception ex)
            {
                MessageBox.Show("无法打开目录：" + ex.Message, "网络延时监测",
                    MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        private void OnAbout(object sender, EventArgs e)
        {
            Version version = Assembly.GetExecutingAssembly().GetName().Version;
            string text =
                "网络延时监测 " + (version == null ? "" : version.ToString()) + "\n\n" +
                "第一个图标：网络延时\n" +
                "  绿色：延时很低，网络很快\n" +
                "  橙色：延时偏高，网络偏慢\n" +
                "  红色：延时过高、严重丢包，或者已经断网\n\n" +
                "第二个图标：CPU 指标（可在菜单里切换温度 / 使用率 / 关闭）\n" +
                "  蓝色：偏低　橙色：中等　红色：偏高\n\n" +
                "左键点击图标打开菜单，双击立即检测。\n\n" +
                "设置文件：\n" + _settings.FilePath;
            MessageBox.Show(text, "关于 网络延时监测", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void OnExit(object sender, EventArgs e)
        {
            _exiting = true;
            try { _monitor.Stop(); }
            catch (Exception) { }
            try { _cpuMonitor.Stop(); }
            catch (Exception) { }
            _slotNetwork.Dispose();
            _slotCpu.Dispose();
            _menu.Dispose();
            _dispatcher.Dispose();
            ExitThread();
        }

        // ---------------------------------------------------------------- 开机启动

        private static bool IsStartupEnabled()
        {
            try
            {
                using (RegistryKey key = Registry.CurrentUser.OpenSubKey(RunKeyPath, false))
                {
                    if (key == null) { return false; }
                    return key.GetValue(RunValueName) != null;
                }
            }
            catch (Exception)
            {
                return false;
            }
        }

        private static void SetStartup(bool enabled)
        {
            using (RegistryKey key = Registry.CurrentUser.CreateSubKey(RunKeyPath))
            {
                if (key == null) { throw new InvalidOperationException("无法写入注册表启动项。"); }
                if (enabled)
                {
                    key.SetValue(RunValueName, "\"" + Application.ExecutablePath + "\"");
                }
                else
                {
                    key.DeleteValue(RunValueName, false);
                }
            }
        }

        // ---------------------------------------------------------------- 收尾

        protected override void Dispose(bool disposing)
        {
            if (disposing && !_exiting)
            {
                _exiting = true;
                try { _monitor.Dispose(); }
                catch (Exception) { }
                try { _cpuMonitor.Dispose(); }
                catch (Exception) { }
                try { _slotNetwork.Dispose(); }
                catch (Exception) { }
                try { _slotCpu.Dispose(); }
                catch (Exception) { }
            }
            base.Dispose(disposing);
        }

        private static string Truncate(string text, int max)
        {
            if (text == null) { return ""; }
            return text.Length <= max ? text : text.Substring(0, max);
        }
    }
}
