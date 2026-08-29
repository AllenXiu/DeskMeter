# DeskMeter 会话交接总结（Session Summary）

> 目的：供**新会话**快速接手本项目。阅读本文档 + `docs/DESIGN.md`（完整需求设计蓝本）即可继续工作。
> 生成：2025 年，DeskMeter 设计与规划阶段结束，P0 实现尚未开始。

---

## 1. 项目概况

| 项 | 内容 |
|---|---|
| 产品 | **DeskMeter** —— Conky 风格的 Windows 桌面系统监控小部件 |
| 仓库 | https://github.com/AllenXiu/DeskMeter（public，MIT） |
| 核心价值 | 透明融入壁纸、命令行式纯文本 + 矢量 bar/graph、配置驱动、无广告遥测 |
| 目标 | **Windows 可监测范围内与 Conky 功能无差异**（配置语法/变量/行为完全兼容） |

## 2. 当前状态（已完成 ✅）

- **文档**（已提交 main，4 commits 全部推送）：
  - `README.md` / `README_zh.md`（产品介绍）
  - `LICENSE`（MIT）
  - `docs/DESIGN.md` —— **核心蓝本**：完整需求 + 与 Conky 同构的架构 + 功能矩阵
- **Pixso 设计稿**：页面「DeskMeter」共 6 帧（见 §5）
- **Git**：`main` 分支 4 commits（e7667f4 → 491961c → bfa9005 → 959ba62）已推送
- **P0 实现**（`dev` 分支，已完成、未合并）：sln + Core/Render/App/Tests 四项目骨架、MoonSharp 配置引擎、Object Tree、WPF 透明置底窗口（`AllowsTransparency` + `WS_EX_TRANSPARENT` + `HWND_BOTTOM`）、Console 渲染后端、29 项单测全绿；可直接加载 `conky-main/data/conky.conf` 渲染（可监测有值、不可监测占位不报错）

## 3. 关键技术决策（已确认，勿轻易变更）

| 决策 | 结论 |
|---|---|
| 技术栈 | **C# / .NET 8 + WPF**（仅 Windows）+ **MoonSharp**（纯 C# Lua 5.2，执行 conky.conf） |
| 许可证 | **MIT**（Conky 是 GPL-3.0，只对齐行为/语法，**不复制源码**，规避传染） |
| 平台 | 仅 Windows 10/11 |
| 无差异范围 | Windows 可监测范围内与 Conky 功能无差异；Linux 专属对象（acpi/mpd/…）语法解析但返回占位 |
| 资源占用 | 接受 .NET 常规（NFR-2：内存 <100MB） |
| Non-goals | 不做 Lua 绘图脚本（Cairo 级）、交互式皮肤、皮肤市场；**配置执行支持完整 Lua**（MoonSharp） |

## 4. 与 Conky 的架构对齐（DESIGN.md §5 已落地）

对照 Conky 源码（本地 `conky-main/`）确认的 **4 个同构机制**：

1. **Lua 解释器执行配置**（≈ Conky lua-config）：`conky.conf` 即完整 Lua 代码，MoonSharp 执行，支持函数/变量/计算/`dofile()`
2. **对象树 + 回调**（≈ Conky text_object / OBJ 宏）：每个 `${var}`/文本片段一个 ObjectNode，含 `Print/Iftest/BarVal/GraphVal/Percentage` 回调；OBJ/OBJ_ARG/OBJ_IF 注册表；支持条件块与嵌套
3. **异步周期回调**（≈ Conky update-cb）：`exec`/`top`/网络用 Task 周期回调，不阻塞主循环
4. **多后端渲染抽象**（≈ Conky display_output_base）：WpfWindow / Console / File / HTTP

分层：`Display Output → Renderer（等宽文本+矢量 bar/graph）→ Object Tree → Data Providers → Config Engine(MoonSharp)`，详见 DESIGN.md §5.1。

**功能差距已补齐**：`cpubar`（非 cpu_bar）、`fs_*` 磁盘系列、参数化 bar/graph（`${cpubar 6}` `${membar 6,120}` `${fs_bar 6 /}`）、命名颜色（`${color grey}`）、swap/freq/processes/top pid/scroll 变量、`minimum_size` 与 `minimum_width/height` 别名、等宽字体默认（Consolas）、鼠标事件（P2）、布局对象（alignc/alignr/goto/hr/offset）。

## 5. Pixso 设计稿（6 帧，均已截图验证）

| 帧 | 内容 |
|---|---|
| **Widget Default** | 命令行风格纯文本 + 矢量 bar/graph：主机名+时钟、`$hr` 分隔线、CPU 12%/内存 26%/磁盘 58%（标签+百分比+进度条）、网络/运行时间、底部 CPU 曲线图 |
| **Desktop Scene** | 1920×1080 浅色壁纸 + 右上角 (16,16) 小部件 |
| **Settings Window**（常规） | 刷新间隔 / 点击穿透 / 开机自启 / 显示器（运行时开关） |
| **Settings 配置** | conky.conf 文件编辑器：26 行完整示例（Consolas、own_window 系列、参数化 bar、`${color grey}`）、行号、语法高亮、垂直滚动条、工具栏（撤销/重做/查找/保存） |
| **Settings 关于** | Logo「D」、版本 v0.1.0、MIT/配置路径/主页/版权 |
| **Tray Interaction** | 底部任务栏 + 系统托盘图标「D」+ 弹出菜单（设置…高亮/编辑配置…/刷新/退出） |

三页设置窗口统一高度 **660**。

**Pixso 环境限制**（已知）：仅 Noto Sans SC 字体；变量无模式切换 API（设计稿用浅色主题）；SVG 内嵌色无法绑变量。

## 6. 参考资源

- Conky 源码：`conky-main/`（本地，仅参考，已加 `.gitignore`）
- Conky 官方文档：https://conky.sourceforge.net/docs.html
- Conky 默认配置：`conky-main/data/conky.conf`（命令行风格实证）
- 使用教程：https://geek-blogs.com/blog/conky-linux/

## 7. 重要踩坑记录（实现/设计时注意）

**Pixso DSL**：
- padding 数组 `[a,b]` 实际语义是 **[垂直, 水平]**（与 schema 文档标注相反）
- `$var` 字面文本会被 DSL 误判为变量引用 → 用占位文本创建 + `eval_script` 写回 `characters`
- grid 方向 autoLayout 不生效 → 用绝对定位手动排
- `M(node, parent, index)` 的 index 是"排到该位元素之前"
- 绑定名必须 ASCII；顶层 I 需 `name=I(...)` 绑定
- 截图验证用 `get_export_image`（临时 URL 立即下载）+ `describe_image` 检查

**Conky 兼容**：
- 变量名必须用 Conky 标准：`cpubar`/`membar`/`swapbar`/`fs_bar`/`fs_free_perc`（不是自定义名）
- 配置键 `minimum_size` 与 `minimum_width`/`minimum_height` 均支持（官方文档与默认配置各用其一）
- 颜色：`default_color` + `color0-9` + `#RRGGBB` + X11 命名颜色

**许可证**：Conky 是 GPL-3.0，本项目 MIT——只参考行为不复制源码。

## 8. 待办事项（新会话从这开始）

1. **P0 实现 ✅（`dev` 分支完成，2025 交接更新）**：骨架 / MoonSharp 配置引擎 / 透明置底窗口 / 文本渲染 + 定时刷新全部完成；验收通过（直接加载 `conky-main/data/conky.conf` 渲染，可监测有值、不可监测占位）。运行方式：
   - `dotnet run --project src/DeskMeter.App -- --config <path>`（WPF 透明小部件）
   - 追加 `--backend console` 无头渲染到 stdout；追加 `--smoke-test` 创建窗口 2.5s 自动关闭
   - 单测：`dotnet test tests/DeskMeter.Tests`
   - **已完成的 P1 子项**：矢量 Bar（Conky 语义：`高度[,宽度]`、默认高 6、宽度省略=填满本行剩余宽度；WPF 圆角矩形描边+填充，console 后端 `#`/`.` 回退）；窗口尺寸按 Conky `text_size` 语义钳制（`minimum_size` / `minimum_width` / `minimum_height` / `maximum_width` 已生效，bar 填满钳制后的宽度），`use_spacer = left|right` 可给动态字段（百分比/字节/速率）补空格防抖动；`${goto N}` 已按 Conky 像素绝对定位实现（计入行宽、console 忽略）；示例配置 `minimum_width = maximum_width = 260` + `${goto 110}` 固定 bar 起点列 → 三条 bar 等长、左右两端对齐、与文字留固定空隙，多次刷新窗口尺寸/位置恒定（268×159 截图验证）
   - **已完成的 P1 子项（续）**：矢量 Graph 曲线图（`${cpugraph}`/`${downspeedgraph}`/`${upspeedgraph}`，`高[,宽]` 参数、默认高 25 宽 0=填满剩余，环形缓冲最近 80 点 FR-VIZ-2，WPF 折线+半透明面积、按系列最大值自动缩放，console 用 `console_graph_ticks=" ,_,=,#"` 回退；示例配置底部已加 `${cpugraph 32}`）；热重载（`FileSystemWatcher` + 300ms 防抖，保存后 1s 内生效，失败保留旧配置 FR-RELOAD-2/FR-CFG-3，`disable_auto_reload` 可关；已用改 color0 蓝→红截图验证）
   - **已完成的 P1 子项（续2）**：`${exec}`/`${execpi}` 异步（cmd /c 执行、3s 超时、失败/未完成显示 `--` 或保留上次输出，不阻塞主循环，已截图验证 exec 输出 hello world）；`${alignc}`/`${alignr N}` 行级排版（渲染层按剩余内容宽度居中/右对齐，console 按 Conky 忽略）；`${scroll N ...}` 滚动（Conky 语义：[方向] 长度 [步长] [间隔] 文本，前缀补空格左移、每帧 step 字符、到尾回绕；右向/wait 简化按左向）；命名颜色全量（System.Drawing.KnownColor 生成标准 X11/CSS 扩展色 ~140 色 + grey/gray 双拼写，已截图验证 peachpuff/cornflowerblue/navajowhite）
   - **已修复的布局 Bug（官方 conky.conf 验证）**：① DrawBar 负尺寸矩形（剩余宽度 <1px 时 DrawRoundedRectangle 抛异常 → 绘制中断、内容残缺"一会几行一会全行"）——已加尺寸守卫（描边 w≥2、填充 fillW≥1、剩余宽度取整）+ Redraw 整块 try/catch 兜底；② 宽度抖动（官方配置无 min/max）——已实现**宽度只增不减**（grow-only，配置重载时 ResetStableWidth 重置，配合 minimum/maximum_width 钳制），实测 203×455 三次恒定；③ `$hr` 后紧跟换行产生空行——NewlineNode 在规则行后变为空操作，分隔线间不再有空行
   - **布局细节修复（官方 conky.conf 验证）**：`$hr` 前有空行时直接把该空行转成规则行（hr 上方不再空一块）；`${top name N}` 按 Conky 语义 top_name_width+1=16 截断+补齐；**关键修复：WPF FormattedText.Width 默认不含尾部空格，导致字符补齐在 WPF 里不占像素宽、列随名字长短漂移——已改用 WidthIncludingTrailingWhitespace（测量与绘制两处），并加 Linux 字体（如 DejaVu Sans Mono）不存在时回退到已安装等宽字体（Consolas/Cascadia Mono），实测四行 top 数据列位一致（PID@197/CPU@249/MEM@295，右对齐字段）**；已像素级验证命名颜色真实渲染
   - **颜色决策（用户确认）**：命名颜色按 Conky/X11 标准值（grey=(190,190,190) 浅灰——Conky color-names.yml 原文；.NET KnownColor.Gray=128 是 Windows 系统灰已覆盖修正）；小字号下浅灰接近白色属正常观感，需要明显深灰时配置用 `${color #808080}`；`${color #FF0000}` 等 hex 与 color0-9 调色板均正常
   - **已知边界**：`$cpu N` 单核暂用总占用；行内 `${font}` 解析不生效；scroll 的 right/wait 方向与 graph 的 -t/-l/-x/-y/-m 旗标按简化处理；offset/voffset/tab 仍为空格近似；网络/CPU 首采样为 0；**内存占用实测约 133MB > NFR-2（<100MB）目标，待优化（P2）**；鼠标点击小部件受置底（HWND_BOTTOM）限制——被其他窗口覆盖时点击会被上层窗口接收（用户环境实测被全屏 Chrome Legacy Window 拦截）
2. **P2 首批 ✅（dev 分支）**：Top 进程（`${top name/pid/cpu/mem N}` 与 `${top_mem ...}`，进程 CPU% 增量采样 + MEM%，列格式与 Conky 一致：name 左对齐 top_name_width+1=16、pid %7i、cpu/mem %6.2f；官方 conky.conf 的 top 行已显示真实进程，截图验证非零 CPU%）；系统托盘（NotifyIcon，菜单=设置…/编辑配置…/刷新/开机自启勾选/退出，UseWindowsForms + Using Remove 消除 WPF/WinForms 二义）；多显示器（deskmeter 扩展块已解析合并，`monitor` 键按 Screen.WorkingArea 定位、超界钳制）；开机自启（HKCU Run 注册表键，容错）。
3. **P2 设置界面 ✅（dev 分支，配置页已按用户决策简化）**：SettingsWindow 三页（常规=刷新间隔/点击穿透/开机自启/显示器下拉；配置页=**内置编辑器已弃用（用户决策：编辑器导入会产生多余空行问题）→ 改为「用系统编辑器打开」（notepad）+ 恢复默认（写回内置配置）；保存时以磁盘当前内容为准，仅写回常规项，热重载自动生效**；关于=版本/MIT/路径/主页）；ConfigWriteBack 纯逻辑写回（update_interval 正则替换/插入、deskmeter 块新建/更新 click_through/monitor，6 条单测）；托盘与鼠标事件（deskmeter.click_through=false 时可点击，单击打开设置）共用 SettingsLauncher 单实例；`--settings` 启动参数用于调试/CI；ConkyCodeEditor 控件文件已删除
3. **P2 剩余**：温度（LibreHardwareMonitor）、鼠标事件（点击弹设置）、主题、设置界面（GUI 三页）
3. **P2**：温度（LibreHardwareMonitor）、Top 进程、鼠标事件（点击弹设置）、托盘、自启、多显示器、主题、设置界面
4. **GitHub 待办**：仓库 About 描述写的是 "C# (Avalonia)"，需改为 WPF（用户手动改，暂缓）

## 9. Git 工作流约定（重要）

- **本地操作**（status/add/commit/log/merge/分支）：DSH 直接执行，无需凭据
- **远程操作**（push/pull/fetch）：**DSH 沙箱读不到 Windows 凭据存储**（SEC_E_NO_CREDENTIALS）→ 需用户批准 DSH 权限升级，或由用户在普通终端执行
- 凭据实际存在（GitHub Desktop 已写入 `git:https://github.com`），用户无需重新登录
- 分支约定：日常在 `dev` 分支开发，完成后合并 `main` 并推送

## 10. 会话关键决策记录（问答回溯）

- 设计稿迭代：命令行风格（用户纠正"不要图片化"→ 后澄清 bar/graph 是矢量非位图，已恢复）
- 设置界面：外观全部配置化（对齐/边距/字体/颜色进 conky.conf）；内容=conky.conf 编辑器（无实时预览，桌面即预览）
- 技术栈：用户会 C++（cocos2dx 背景）但确认保持 C#/.NET + WPF（GPL 传染 + 开发效率考量）
- 弹窗：用户澄清为**系统托盘图标**点击弹菜单（非桌面小部件点击）
