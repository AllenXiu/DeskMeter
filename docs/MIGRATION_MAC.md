# DeskMeter → macOS 迁移小结（阶段式推进，工作区改动未提交 git）

> 目标机：Apple Silicon Mac（macOS 26.6.2），.NET SDK 8.0.424（本机临时装于 /tmp/dotnet-sdk）。
> 结论：现代 .NET 在 macOS 原生支持；本迁移是「解耦 + 替换」而非重写——保留全部 Core 纯逻辑，
> 用 Avalonia 替换 WPF/WinForms，用 macOS 采集后端替换 Windows 采集后端。

## 已完成的阶段0：平台解耦（验收：macOS 上 130/130 测试全绿）

| 改动 | 说明 |
|---|---|
| src/DeskMeter.Core | TFM `net8.0-windows` → `net8.0`；移除 LibreHardwareMonitorLib / PerformanceCounter 包（随 Windows 采集一并迁出） |
| src/DeskMeter.Core.Windows（新） | `net8.0-windows`；收纳 SystemDataCollector.cs / TemperatureMonitor.cs（命名空间不变，Windows App 无需改代码即可引用） |
| src/DeskMeter.App | 增加对 DeskMeter.Core.Windows 的引用（WPF/WinForms 主体保持 Windows 侧） |
| tests | TFM → `net8.0`；磁盘根/换行/exec shell 断言按平台修正；ExecNode/ExecOutputCache 改为 `cmd.exe`(Win) / `/bin/sh -c`(Unix) |
| 验证 | `dotnet test` macOS 130/130 通过；`dotnet build DeskMeter.Core.Windows -p:EnableWindowsTargeting=true` 交叉编译 0 错误 |

## 已完成的阶段1a：macOS 指标后端（src/DeskMeter.Core.Mac）

`MacSystemDataCollector : IDisposable`，产出与 Windows 版同一 `SystemSnapshot`，冒烟（src/DeskMeter.SmokeMac）实测：

- CPU 总占用 + 每核占用（libSystem mach：`host_statistics` flavor 3 / `host_processor_info`）✅
- 内存/交换（`sysctl hw.memsize/pagesize` + `vm_stat`，已用 ≈ active+wired+compressed）✅
- 磁盘（`df -kP` 多挂载点 → `SystemSnapshot.SetDisk`）✅
- 网络（`netstat -ibn` 字节差分 → 速率/累计/默认接口）✅
- 进程 Top（跨平台 `Process.TotalProcessorTime` 差分 + WorkingSet64）✅
- 系统信息（sw_vers/uname/sysctl/RuntimeInformation）✅
- 电池（`pmset -g batt`，台式机无电池返回占位）✅

v1 暂不提供（返回 0/空，占位显示，绝不崩溃）：温度（LHM 不支持 mac）、每进程磁盘 IO / GPU / 连接数（macOS 无公开 API）、网关/DNS 详情。

## 已完成的阶段1b：Avalonia 应用（src/DeskMeter.App.Mac）

- Avalonia 11.3.20（纯代码 UI，无 XAML）；透明、无边框、无任务栏窗口
- 复用 Core 全链路：conky.conf(Lua) → ObjectRegistry/ConkyTextParser → WidgetLayout → 文本渲染（Menlo 等宽）
- `MacWindowHacks`：ObjC 运行时直调 `setIgnoresMouseEvents:` 与 `setLevel:`（kCGDesktopWindowLevel）实现点击穿透 + 桌面层（壁纸之上/图标之下）
- 每 `update_interval` 秒刷新并对齐（alignment/gap_x/gap_y 与 Windows 语义一致）
- 运行验证：`--console-dump` 无头输出真实指标 ✅；`--smoke 5` GUI 打开/关闭退出码 0 ✅（截屏受录屏权限限制未取得，请肉眼确认右上角白色小字）
- 未做（后续阶段）：矢量 bar/graph 的 Avalonia 绘制移植、托盘/菜单栏、多配置管理、`.app` 打包/公证、CI(macos runner) 与安装说明

## 本地运行方式（本机）

```bash
export DOTNET_ROOT=/tmp/dotnet-sdk PATH=/tmp/dotnet-sdk:$PATH
# 无头看输出
dotnet run --project src/DeskMeter.SmokeMac -c Release
# GUI（默认加载 samples/conky.mac.conf —— 仅含 macOS 支持变量的示例，右上角）
dotnet run --project src/DeskMeter.App.Mac -c Release
# GUI 5 秒自动退出（冒烟）
dotnet run --project src/DeskMeter.App.Mac -c Release -- --smoke 5
# 全部测试
dotnet test DeskMeter.Mac.sln -c Release
```

> Windows 侧：DeskMeter.sln 仍保留 WPF 版；拆出的 DeskMeter.Core.Windows 已加回该解决方案。
> 本仓库所有改动均未提交（git status 可见）。
## 阶段2（本轮）成果

- **矢量渲染**：新增 [DeskMeter.Render.Mac](src/DeskMeter.Render.Mac)（Avalonia DrawingContext），把 WPF `WidgetVisual` 移植为 `WidgetVisualAvalonia`——彩色文本 run、圆角矢量 bar、面积/折线 graph、goto/offset/tab/align 语义全保留；字体映射（Consolas 等 → Menlo），无 XAML。
- **托盘/菜单栏**：Avalonia `TrayIcon`（macOS NSStatusItem）→ 菜单含 Top 排序 / 立即刷新 / 退出；托盘图标代码生成。
- **采集补齐**：默认网关（`route -n get default`）与 DNS（`scutil --dns`）填充到快照。
- **打包**：[scripts/package-macos.sh](scripts/package-macos.sh)：自包含 publish → DeskMeter.app（Info.plist 含 LSUIElement 无 Dock 图标）→ ad-hoc codesign → zip。实测产出 ~40MB `DeskMeter-0.2.0-mac-osx-arm64.zip`。
- **CI**：[ci.yml](.github/workflows/ci.yml) 增加 `macos-latest` 的 build-test-macos job（restore/build/test DeskMeter.Mac.sln）。
- **验证**：macOS 130/130 测试通过；GUI `--smoke` 退出码 0（矢量渲染 + 托盘初始化无异常）；`--snapshot` 离屏渲染 PNG 像素统计 10% 不透明（默认白 + grey/lightgrey 配色）确认真实绘制。

## 已知缺口（后续可选）

- **列对齐修复**：Avalonia FormattedText.Width 不含尾部空格（与 WPF 一致），补齐空格的列宽为 0 导致进程表坍缩；已按 run 尾部空格数 × 空格前进步进补宽（RunWidth）。
- **macOS 专用示例**：samples/conky.mac.conf（仅系统/运行/CPU/内存/Swap/磁盘/网络/接口/IP/单核/进程表 name-pid-cpu-mem），默认配置优先加载它；samples/conky.conf 是 Windows 专用示例，温度/每进程 GPU・磁盘 IO・频率等在 mac 上按设计显示占位。
- **进程表表头对齐**：数据行 ${top …} 字段之间不能加空格——表头 ${top_header} 按 name(16)+pid(7)+cpu(6)+mem(6) 无缝拼接，数据行也必须无缝拼接（字段自带 PadLeft/PadRight），否则每列整体右偏一格。
- **窗口层级**：默认 pinned=true 钉在桌面图标层（kCGDesktopIconWindowLevel = -2147483622，壁纸之上、应用窗口之下，不遮挡其它应用）；纯桌面层（-2147483623）在部分 macOS 会被壁纸盖住故弃用。`--window-level overlay` 或 `pinned=false` 可改浮动层。
- **示例布局**：进度条各自独占一行（标签行 + `${…bar 4}` 行），进程表字段间不加空格以与表头对齐。
- 桌面层/点击穿透的视觉效果需在真实桌面确认（本环境无法截屏）；
- 温度（SMC）、每进程磁盘/GPU/连接数（macOS 无公开 API）；
- release.yml 的 mac 产物 job、Developer ID 签名/公证（notarization）；
- CPU 频率在 Apple Silicon 无公开读数（sysctl 为 0，保持占位 `--`）；
- Windows 侧代码与 DeskMeter.sln 的最终回归构建（需 Windows runner/本机）。

> 以上所有改动均未提交（git status 可见），可随时回退。