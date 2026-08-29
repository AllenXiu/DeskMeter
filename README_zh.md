# DeskMeter

> 一款 Conky 风格的 Windows 桌面系统监控工具。
> 轻量。透明。与你的壁纸融为一体。无广告，无冗余功能。

[English](README.md) | 中文

## 功能特性

- **纯文本显示系统指标** — CPU、每核心占用、内存、磁盘、网络上下行、运行时间、时钟、主机名、Windows 版本等，通过简单的 `${var}` 占位符驱动，和 Conky 一样
- **可视化仪表** — 进度条（`${cpu_bar}`）和历史曲线图（`${cpugraph}`、`${downspeedgraph}`）
- **融入桌面** — 透明、无边框、可点击穿透的窗口，固定在桌面层
- **灵活布局** — 四角对齐预设与 X/Y 间距偏移，支持自定义字体和逐行颜色
- **`${exec}` 扩展** — 运行任意命令并显示其输出
- **兼容 Conky 配置** — 直接套用你现有的 `conky.conf`（支持其子集）即可运行
- **热重载** — 编辑配置文件，桌面即刻实时更新
- **轻量设计** — 单进程，默认约 2 秒刷新，占用极小
- **开源。无广告。无遥测。无安装包捆绑。**

## 文档

- [变量/系统数据接口参考](docs/VARIABLES.md)（96 个变量）与 [Conky 覆盖对比](docs/CONKY_COMPAT.md)（429 vs 96）
- [设计文档](docs/DESIGN.md) 与 [会话交接总结](docs/SESSION_SUMMARY.md)

## 安装 / 发布

- 从 [GitHub Releases](https://github.com/AllenXiu/DeskMeter/releases) 下载 `DeskMeter-<版本>-win-x64.zip`，解压即用（自包含单文件，无需安装 .NET）。
- 首次启动自动把 `samples/conky.conf` 导入为默认配置（配置库位于 `%APPDATA%\DeskMeter\configs`）。
- 发布流程：推送到 `v*` 标签（如 `v0.1.0`）→ GitHub Actions 自动 构建 + 测试 + 打包 → 生成 Release。

## 路线图

- [x] P0：文本变量、透明桌面窗口、conky.conf 解析、定时刷新
- [x] P1：矢量进度条与曲线图、颜色、`${exec}`、热重载、布局对象
- [x] P2：温度（LibreHardwareMonitor）、进程 Top 榜、托盘、开机自启、多显示器、多配置管理（替代已取消的"主题"）、设置界面
- [x] 边界项：行内 `${font}`、scroll right/wait、graph 旗标（-l/-m/-i/-y）、offset/voffset/tab 像素语义
- [ ] 未来：Linux 支持（同一配置格式，/proc + lm-sensors 数据源）

> 非目标：不支持 Lua 绘图脚本、不支持鼠标交互皮肤、不做皮肤市场。DeskMeter 有意保持简洁。

## 开源许可

本项目基于 [MIT](LICENSE) 许可协议开源。
