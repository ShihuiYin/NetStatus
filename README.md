# NetStatus · Windows 任务栏网络延时 + CPU 监测

在 Windows 任务栏托盘上实时显示**网络延时**和 **CPU 温度（或使用率）**，用颜色一眼看出好坏。
程序是单个原生 exe，用 Windows 自带的编译器编译，不装运行时、不装依赖、不联网、无后台服务。

## 两个图标

**图标一：网络延时**（数值 + `ms`）

| 颜色 | 含义 | 默认判定条件 |
| --- | --- | --- |
| 🟢 绿色 | 很快 | 最好延时 ≤ 80 ms，且丢包 ≤ 25% |
| 🟠 橙色 | 偏慢 | 最好延时 ≤ 250 ms（或丢包 25% ~ 60%） |
| 🔴 红色 | 卡顿 / 无法连接 | 延时 > 250 ms、丢包 ≥ 60%，或连续 3 次完全无响应、系统无网络 |
| ⚪ 灰色 | 检测中 / 已暂停 | — |

断网时图标显示 `×`；延时 ≥ 1000 ms 显示成 `1s`。

**图标二：CPU 指标**（数值 + `°C` 或 `%`，可在菜单里切换温度 / 使用率 / 关闭）

| 颜色 | 含义 | 温度默认阈值 | 使用率默认阈值 |
| --- | --- | --- | --- |
| 🔵 蓝色 | 偏低 | ≤ 60 °C | ≤ 40% |
| 🟠 橙色 | 中等 | 60 ~ 80 °C | 40 ~ 80% |
| 🔴 红色 | 偏高 | > 80 °C | > 80% |
| ⚪ 灰色 | 读不到温度 | — | — |

数值下面的小字是单位；不想看数字或单位，可以在菜单里关掉。

## 快速开始

1. 把整个项目文件夹放到 Windows 上（例如 `D:\code\netstatus`）。
2. 双击 **`build.bat`**。它用 Windows 自带的 C# 编译器（`%WINDIR%\Microsoft.NET\Framework64\v4.0.30319\csc.exe`），
   **不需要 .NET SDK、不需要联网、不下载任何依赖**，2 秒左右出结果，并自动把 exe 部署到
   `C:\Tools\NetStatus\`。如果 NetStatus 正在运行，它会先问你要不要自动退出并重启，
   省得自己去托盘里退出。部署目录可以在 `build.bat` 顶部那一行改，或用环境变量 `NETSTATUS_DIR` 覆盖。
   项目就算放在 WSL 的 `\\wsl.localhost\...` 网络路径上也能一键编译 —— 脚本会先自动镜像到本地临时目录
   （csc.exe 没法直接在网络路径上写临时文件），编译完还会把 exe 同步回项目的 `dist\`。
3. 双击 **`run.bat`**（启动已部署的那份）或 **`dist\NetStatus.exe`** 运行。
4. 图标出现在任务栏右下角；没看到就点任务栏的 `^`，把图标拖到常显区。
   建议把 exe 放到固定目录（例如 `C:\Tools\NetStatus\`），开机启动和升级都更方便。

首次运行会弹一个说明气泡，并在 `%APPDATA%\NetStatus\settings.ini` 生成设置文件。

> exe 没有代码签名，首次运行 SmartScreen 可能提示"已保护你的电脑"：点"更多信息 → 仍要运行"。
> 源码就在本仓库里，可以自己核对、自己编译。

## 使用方法

- **左键 / 右键点击任一图标**：打开菜单；**双击**：立刻检测一次。
- 菜单内容：
  - 前三行是当前状态：网络延时 / 探测目标与统计 / CPU 指标
  - `立即检测`、`暂停监测`
  - `在图标上显示数值`、`显示单位`、`状态变化时通知我`
  - `显示 CPU 图标`、`CPU 指标 ▸ 温度 / 使用率 / 关闭采样`、`为什么读不到 CPU 温度？`
  - `开机自动启动`、`打开设置文件`、`重新载入设置`、`打开数据目录`、`关于`、`退出`

## CPU 温度为什么可能读不到

Windows 没有统一的 CPU 温度接口，程序会依次尝试：

1. **LibreHardwareMonitor / OpenHardwareMonitor 的 WMI**（`root\LibreHardwareMonitor`、`root\OpenHardwareMonitor`）
2. **ACPI 热区**（`MSAcpi_ThermalZoneTemperature`，多数笔记本可用）
3. **热区性能计数器**（`Win32_PerfFormattedData_Counters_ThermalZoneInformation`，少数机器可用）

如果都读不到，图标显示灰色 `--`，右键菜单里有 `为什么读不到 CPU 温度？` 会显示每个来源的具体错误。
这种情况下：

- **台式机 / 迷你主机（AMD Ryzen 平台尤其常见）**：固件根本不向外暴露温度，用户态程序无论如何都读不到。
  需要装 [LibreHardwareMonitor](https://github.com/LibreHardwareMonitor/LibreHardwareMonitor) 或
  OpenHardwareMonitor（建议以管理员运行），开启其 WMI 支持后本程序会自动识别，无需额外配置。
- 或者把 `CPU 指标` 改成 **使用率**：它用系统计数器实现，不依赖任何外部工具，永远可用。

### 让 OpenHardwareMonitor 开机自启（推荐做法）

OpenHardwareMonitor 必须**以管理员权限运行**才能加载 WinRing0 驱动读到温度，所以**不能**直接丢进
「启动」文件夹（那里是普通权限，温度照样读不到）。正确做法是注册一个「登录时以最高权限运行」的计划任务：

1. 把 OpenHardwareMonitor 整个文件夹放到固定目录，例如 `C:\Tools\OpenHardwareMonitor`。
   不要留在「下载」里 —— 清理下载文件夹会让它失效（驱动注册表项会指向不存在的路径）。
2. 以**管理员身份**打开 Windows PowerShell，执行：

   ```powershell
   $exe  = 'C:\Tools\OpenHardwareMonitor\OpenHardwareMonitor.exe'
   $user = "$env:USERDOMAIN\$env:USERNAME"
   $act  = New-ScheduledTaskAction -Execute $exe -WorkingDirectory (Split-Path $exe)
   $trg  = New-ScheduledTaskTrigger -AtLogOn -User $user
   $trg.Delay = 'PT15S'          # 稍等一下再启动，避开开机高峰
   $pri  = New-ScheduledTaskPrincipal -UserId $user -LogonType Interactive -RunLevel Highest
   $set  = New-ScheduledTaskSettingsSet -AllowStartIfOnBatteries -DontStopIfGoingOnBatteries `
             -StartWhenAvailable -ExecutionTimeLimit ([TimeSpan]::Zero)
   Register-ScheduledTask -TaskName OpenHardwareMonitor -Action $act -Trigger $trg `
             -Principal $pri -Settings $set -Force
   ```

3. 验证：

   ```powershell
   Start-ScheduledTask -TaskName OpenHardwareMonitor
   Get-Process OpenHardwareMonitor | Select-Object Id, Path
   ```

   再确认驱动指向新目录（`ImagePath` 应为 `\??\C:\Tools\OpenHardwareMonitor\OpenHardwareMonitorLib.sys`）：

   ```powershell
   reg query 'HKLM\SYSTEM\CurrentControlSet\Services\WinRing0_1_2_0' /v ImagePath
   ```

取消开机自启：

```powershell
Unregister-ScheduledTask -TaskName OpenHardwareMonitor -Confirm:$false
```

小提示：

- 想让它启动后**最小化到托盘**而不是占用任务栏：在 OHM 的 `Options` 菜单里勾选
  `Start minimized` 和 `Minimize to tray`，它会自己记住。
- 计划任务里用 `wscript` + 一个 `.vbs`（`sh.Run "…\OpenHardwareMonitor.exe", 7, False`）
  启动可以做到「没有黑框闪烁 + 直接最小化」。
- Windows 会**静默忽略**「注册表 Run 键 + 需要提权」的启动项，所以别用 Run 键或启动文件夹。

## 设置说明

设置文件：`%APPDATA%\NetStatus\settings.ini`（UTF-8，可直接用记事本改）。
改完保存后，在图标菜单里选 **重新载入设置** 即可生效，不用重启程序。

### 网络

| 键 | 默认值 | 说明 |
| --- | --- | --- |
| `Targets` | `223.5.5.5, 119.29.29.29, 120.53.53.53, 180.76.76.76` | 探测目标，逗号分隔，可填 IP 或域名。每轮并行探测，取**最快**的一个作为当前延时 |
| `IntervalMs` | `1500` | 探测间隔（毫秒） |
| `TimeoutMs` | `1000` | 单次探测超时（毫秒） |
| `FailThreshold` | `3` | 连续多少次完全无响应后判定为"卡顿 / 无法连接"（红色） |
| `TcpFallback` | `true` | ping 全部失败时改用 TCP 连接测试，避免因 ICMP 被屏蔽而误报断网 |
| `TcpHost` / `TcpPort` | `www.baidu.com` / `443` | TCP 兜底目标 |
| `FastMs` | `80` | ≤ 该值显示绿色 |
| `SlowMs` | `250` | ≤ 该值显示橙色，再高显示红色 |
| `LossOrangePercent` | `25` | 当轮丢包率超过它，至少显示橙色 |
| `LossRedPercent` | `60` | 当轮丢包率超过它，显示红色 |

### CPU

| 键 | 默认值 | 说明 |
| --- | --- | --- |
| `ShowCpuIcon` | `true` | 是否显示第二个图标 |
| `CpuSensor` | `temperature` | `temperature` = 温度，`load` = 使用率，`off` = 不采样 |
| `CpuIntervalMs` | `2000` | 采样间隔（毫秒） |
| `CpuLowC` / `CpuHighC` | `60` / `80` | 温度 ≤ 低值为蓝色，> 高值为红色，中间橙色 |
| `CpuLowLoad` / `CpuHighLoad` | `40` / `80` | 使用率的分档同上 |

### 界面

| 键 | 默认值 | 说明 |
| --- | --- | --- |
| `ShowNumber` | `true` | 是否在图标上显示数值（关掉只显示彩色圆点） |
| `ShowUnit` | `true` | 数值下面是否显示单位（`ms` / `°C` / `%`） |
| `NotifyOnStateChange` | `true` | 网络变差 / 恢复时弹气泡提醒 |
| `HistorySize` | `60` | 统计窗口保留多少轮结果 |

## 常见问题

**网络图标一直是红色？**
1. 先确认是不是真的断网（`ping 223.5.5.5`）。
2. 如果 ping 不通但能上网，多半是网络屏蔽了 ICMP：确认 `TcpFallback = true`。
3. 换个探测目标——有些公共 DNS 在部分网络（公司 / 校园网）下会被屏蔽，
   被屏蔽的目标每轮都会白等一个超时。右键菜单详情行在全部失败时会列出失败目标。

**CPU 图标是灰色 `--`？** 见上面「CPU 温度为什么可能读不到」。

**数字太小看不清？**
托盘图标本身很小：100% 缩放时只有 16×16，150% 缩放时是 24×24（本程序是 DPI 感知的，会按系统缩放画）。
程序按「字形实际高度」而不是「字体行高」来定字号，已经把这个尺寸下能用的像素榨干了。
嫌挤的话可以：菜单里关掉单位只留数字、关掉数值只留颜色，或者把阈值调成只关心蓝/橙/红。

**开机启动没用？**
开机启动写的是 exe 的完整路径。请先把 exe 放到 Windows 上的真实目录，
不要放在 WSL 的 `\\wsl.localhost\...` 路径里（那种路径要等 WSL 起来才可用）。

开机启动项是注册表里的 `HKCU\Software\Microsoft\Windows\CurrentVersion\Run\NetStatus`，
里面存的是 exe 的**绝对路径**。所以**移动 exe 之后要在菜单里重新勾选一次「开机自动启动」**
（或直接把那个注册表值改成新路径），否则开机时系统会找不到程序。

**换目录的标准步骤**：退出程序 → 复制 exe 到新目录 → 双击新目录里的 exe → 右键图标取消再勾选
「开机自动启动」→ 删掉旧 exe。

**怎么卸载？**
右键图标 → 退出；取消勾选"开机自动启动"；删除 exe 和 `%APPDATA%\NetStatus` 目录。
程序只写注册表里的开机启动项（你主动开启时），不留后台服务。

## 用 .NET SDK 构建（可选）

装了 .NET SDK 8+ 的话：

```bat
dotnet publish NetStatus.csproj -c Release
```

## 项目结构

```
build.bat                 一键编译 + 自动部署到 C:\Tools\NetStatus（零依赖）
run.bat                   启动已部署的 exe（找不到就先编译）
NetStatus.csproj          可选的 .NET 8 SDK 工程
src/Program.cs            入口：单实例检查、异常日志
src/TrayApp.cs            两个托盘图标、菜单、气泡提醒、开机启动
src/TrayIconSlot.cs       单个托盘图标位置（图标句柄管理）
src/TrayIconRenderer.cs   图标绘制（圆角底色 + 数值 + 单位）
src/IconFactory.cs        位图 → 带 alpha 的图标句柄
src/LatencyMonitor.cs     网络探测线程、状态判定与统计
src/LatencyProbe.cs       单轮探测：并行 ping + TCP 兜底
src/CpuMonitor.cs         CPU 采样线程
src/CpuSensor.cs          CPU 温度 / 使用率读取（多来源回退）
src/AppSettings.cs        INI 设置读写
src/LatencyLevel.cs       网络等级与颜色（绿/橙/红）
src/MetricLevel.cs        CPU 等级与颜色（蓝/橙/红）
src/app.manifest          DPI 感知等进程设置
```
