# DeskMeter 需求与设计文档

> 版本：v0.1（草稿） · 状态：评审中
> 配套：`README.md`（产品介绍）、Pixso 画布「DeskMeter」页面（UI 设计稿）

---

## 1. 产品概述

| 项 | 内容 |
|---|---|
| 产品名 | DeskMeter |
| 定位 | Conky 风格的 Windows 桌面系统监控小部件 |
| 核心价值 | 轻量、透明、融入壁纸、无广告无遥测 |
| 目标用户 | 喜欢桌面美化/极简信息展示的用户；Conky 老用户迁移 |
| 平台 | Windows 10/11（桌面层） |
| 形态 | 单进程、常驻桌面的透明小部件窗口 |

### 设计原则（优先级从高到低）

1. **透明融入** —— 小部件视觉上是"印在壁纸上"的，而不是一个应用窗口
2. **轻量** —— 单进程、刷新可控、占用可忽略
3. **简单** —— 一个配置文件（完整 Lua）、一套 `${var}` 语法，学习成本低
4. **克制** —— 明确不做：Lua 绘图脚本（Cairo 级）、交互式皮肤、皮肤市场、广告；**配置执行支持完整 Lua**（MoonSharp，与 Conky 同构）

---

## 2. 核心概念

| 概念 | 说明 |
|---|---|
| **Widget（小部件）** | 一个透明、无边框、点击穿透、固定于桌面层的窗口 |
| **Config（配置）** | Conky 兼容的 Lua 配置文件（`conky.conf`，完整 Lua + MoonSharp 执行） |
| **Variable（变量）** | 文本中的 `${var}` 占位符，由系统指标实时替换 |
| **Bar / Graph（条/图）** | 矢量绘制的进度条与历史曲线，支撑 `${cpubar}`、`${cpugraph}` 等变量 |
| **刷新循环** | 定时（默认 2s）采集数据并重绘；配置文件变化时热重载 |

---

## 3. 功能需求

### 3.1 指标变量（FR-VAR）

> 语法：`${name}`，大小写不敏感。示例值仅示意格式。

| 变量 | 含义 | 示例输出 |
|---|---|---|
| `${hostname}` | 计算机名 | `DESKTOP-ABC123` |
| `${sysname}` | 系统名称 | `Microsoft Windows 11 Pro` |
| `${kernel}` | 版本号 | `10.0.22631` |
| `${uptime}` | 运行时间 | `3d 4h 12m` |
| `${time %H:%M}` | 时钟（格式串可选） | `14:35` |
| `${date %Y-%m-%d}` | 日期（格式串可选） | `2025-01-01` |
| `${cpu}` | CPU 总占用 % | `12` |
| `${cpu N}` | 第 N 核占用 % | `8` |
| `${cpubar}` / `${cpubar H[,W]}` | CPU 进度条（H=高度、W=宽度，可省略） | 图形 |
| `${cpugraph H,W}` | CPU 历史曲线（可带尺寸参数） | 图形 |
| `${mem}` / `${memmax}` | 内存已用 / 总量 | `4.2GiB` / `16GiB` |
| `${memperc}` | 内存占用 % | `26` |
| `${membar}` | 内存进度条 | 图形 |
| `${fs_used /}` | 磁盘已用（可指定路径） | `120GiB` |
| `${fs_free /}` | 磁盘剩余 | `84GiB` |
| `${fs_size /}` | 磁盘总量 | `204GiB` |
| `${fs_free_perc /}` | 磁盘剩余 % | `41` |
| `${fs_bar /}` | 磁盘进度条 | 图形 |
| `${downspeed}` / `${upspeed}` | 网络下行 / 上行速率 | `2.4MiB/s` / `120KiB/s` |
| `${downspeedgraph}` | 下行速率历史曲线 | 图形 |
| `${totaldown}` / `${totalup}` | 累计流量 | `12.5GiB` / `3.1GiB` |
| `${top name N}` | CPU 占用第 N 高的进程名 | `firefox` |
| `${top cpu N}` | 对应进程 CPU % | `12.4` |
| `${top_mem name N}` / `${top_mem mem N}` | 内存占用第 N 高的进程名 / % | `chrome` / `8.2` |
| `${top pid N}` | CPU 第 N 高进程 PID | `4821` |
| `${swap}` / `${swapmax}` / `${swapperc}` / `${swapbar}` | 交换分区已用 / 总量 / % / 进度条 | `4.1GiB` / `16GiB` / `26` |
| `${freq}` / `${freq_g}` | CPU 频率（MHz / GHz） | `3600` / `3.60` |
| `${processes}` / `${running_processes}` | 进程总数 / 运行中进程数 | `245` / `3` |
| `${scroll N ...}` | 文本在 N 字符宽度内循环滚动 | 滚动文本 |
| `${color}` / `${color N}` / `${color #RRGGBB}` / `${color grey}` | 行内颜色切换（N=0-9 调色板、X11 命名颜色如 `grey`/`lightgrey`；恢复默认用 `${color}`） | 文本着色 |
| `${font ...}` | 行内字体切换（含 bold/italic） | 文本变体 |
| `${alignc}` / `${alignr N}` / `${goto N}` | 文本居中 / 右对齐（预留 N 宽）/ 跳转到第 N 列 | 布局 |
| `${offset N}` / `${voffset N}` / `${tab N}` | 水平 / 垂直偏移、制表位 | 布局 |
| `${hr}` / `${newline}` | 水平分隔线 / 强制换行 | 布局 |
| `${exec <cmd>}` | 执行命令并显示输出 | 任意文本 |
| `${execpi N <cmd>}` | 每 N 秒执行一次命令（如 `${execpi 300 ...}`） | 任意文本 |

**FR-VAR-1**：上述变量在 `TEXT` 段中按行展开，支持常量文本混排与换行。
**FR-VAR-2**：变量解析失败时显示占位文本（如 `--`）而不是崩溃。
**FR-VAR-3**：`${time}`/`${date}` 支持 `%` 格式串（`strftime` 子集）。

### 3.2 可视化元素（FR-VIZ）

| 元素 | 变量 | 说明 |
|---|---|---|
| 进度条 Bar | `${cpubar H,W}` `${membar H,W}` `${swapbar H,W}` `${fs_bar H,W /}` | 矢量横条（轨道+填充），语法 `\${bar 高[,宽]}`，如 `${cpubar 4}` `${membar 4,120}`；颜色/尺寸可配置 |
| 曲线图 Graph | `${cpugraph H,W}` `${downspeedgraph H,W}` | 矢量折线/面积图，语法 `\${graph 高,宽 [渐变1 渐变2] [缩放]}` |
| 文本颜色 | 配置项 | 每行颜色、默认色、`color0-9` 调色板 |

**FR-VIZ-1**：Bar/Graph 支持 Conky 标准尺寸参数（`${cpubar 4}` = 高 4px；`${membar 4,120}` = 高 4 宽 120；`${fs_bar 6 /}` = 高 6 + 路径参数），省略尺寸时用 `default_bar_size` / `default_graph_size`；渐变、缩放同 Conky 语法。
**FR-VIZ-2**：Graph 曲线图滚动显示最近 N 个采样点（默认 80）。

### 3.3 窗口与显示（FR-WIN）

| 需求 | 说明 | 优先级 |
|---|---|---|
| 透明背景 | 窗口背景完全透明，无边框、无标题栏 | P0 |
| 点击穿透 | 鼠标点击穿过小部件落到下层应用 | P0 |
| 置底 | 固定于桌面层（`HWND_BOTTOM` 且不响应激活） | P0 |
| 角落对齐 | `top_left / top_right / bottom_left / bottom_right` 预设 | P0 |
| 位置偏移 | `gap_x`、`gap_y`（相对屏幕边缘的像素偏移） | P0 |
| 自定义字体 | 字体族、字号、加粗/斜体（含 `${font}` 行内切换） | P1 |
| 窗口尺寸 | `minimum_size` / `maximum_width`（官方键名；内容自适应基础上限制下限/上限） | P1 |
| 窗口边框 | `draw_borders` / `border_width` / `border_color` | P1 |
| 窗口提示 | `own_window_hints`（undecorated / below / sticky / skip_taskbar / skip_pager） | P1 |
| 后台运行 | `background = true`（后台常驻，无额外窗口） | P1 |
| 多显示器 | 选择在哪个显示器显示 | P2 |

**FR-WIN-1**：窗口尺寸由内容自适应（`fit_content`），无需手填像素。
**FR-WIN-2**：对齐位置或配置变化后，窗口自动重定位（含热重载路径）。

### 3.4 配置系统（FR-CFG）

**支持的 `conky.conf`（与 Conky 完全一致）**：配置文件即 **Lua 代码**，由 **MoonSharp** 解释器执行（等价 Conky lua-config）——支持注释、函数、变量、计算、`dofile()` 模块化；未知键忽略（FR-CFG-1）。配置键名与官方全量对齐（见 3.4.1 矩阵）；`minimum_size` 与 `minimum_width`/`minimum_height` **别名共存**（官方默认配置即用后者）。

```conf
conky.config = {
    update_interval = 2,          -- 刷新秒数
    alignment = 'top_right',      -- 四角对齐
    gap_x = 16, gap_y = 16,       -- 边缘偏移
    own_window = true,
    own_window_type = 'desktop',  -- desktop/dock/normal
    own_window_transparent = true,
    own_window_hints = 'undecorated,below,sticky,skip_taskbar,skip_pager',
    use_xft = true,
    font = 'Consolas:size=12',    -- 等宽字体（中文自动 fallback）
    default_color = 'FFFFFF',     -- 默认文字色
    color0 = '88CCFF',            -- 调色板
    color1 = 'FF8888',
    -- ... color2..9
    minimum_size = 200,           -- 别名：minimum_width / minimum_height 亦支持
    draw_borders = false,         -- 边框
    border_width = 1,
};
conky.text = [[
${color0}$hostname${color}  $time %H:%M
$hr
${color grey}CPU$color  $cpu%  $cpubar 6
内存  $memperc%  $membar 6,120
磁盘  $fs_free_perc /%  $fs_bar 6 /
网络  ↓ $downspeed  ↑ $upspeed
运行时间  $uptime
]];
```

**DeskMeter 扩展（P1）**：

```conf
-- DeskMeter 专用配置块（conky 会忽略未知键，保证向后兼容）
deskmeter = {
    graph_height = 32,           -- 曲线图高度
    bar_width = 120,             -- 进度条宽度
    line_gap = 4,                -- 行间距
    click_through = true,        -- 点击穿透
    monitor = 0,                 -- 显示器编号
};
```

**FR-CFG-1**：解析器忽略未知键与注释，未知键不报错（兼容未来 conky 版本）。
**FR-CFG-2**：`TEXT` 段中的 `\n` 换行、`${}` 转义（`$$`）正确处理。
**FR-CFG-3**：配置文件非法时，保留上一次成功配置并提示错误位置，不闪退。

#### 3.4.1 官方配置项支持矩阵（对照 Conky 官方文档全量）

> 目标：现有 `conky.conf` 可**原样读入**；✅ 完整支持，◐ 部分支持/平台映射，❌ 解析但忽略（不报错）。

| 类别 | 配置项 | 状态 |
|---|---|---|
| 窗口 | `alignment`（9 向 + tl/tr/tm/bl/br/bm/ml/mm/mr 缩写）、`own_window`、`own_window_type`、`own_window_transparent`、`own_window_argb_visual`、`own_window_argb_value`、`own_window_hints`、`own_window_class`、`own_window_title`、`own_window_colour`、`background`、`double_buffer`、`minimum_size`、`maximum_width`、`gap_x`、`gap_y` | ✅ P0-P1 |
| 边框 | `draw_borders`、`border_width`、`border_inner_margin`、`border_outer_margin`、`stippled_borders`、`draw_graph_borders` | ✅ P1 |
| 字体颜色 | `font`、`use_xft`、`xftfont`、`xftalpha`、`default_color`、`color0-9`、`default_outline_color`、`default_shade_color`、`draw_outline`、`draw_shades` | ✅ P0-P1 |
| 更新采样 | `update_interval`、`update_interval_on_battery`、`total_run_times`、`cpu_avg_samples`、`net_avg_samples`、`diskio_avg_samples` | ✅ P0-P1 |
| 文本格式 | `use_spacer`、`uppercase`、`pad_percents`、`format_human_readable`、`short_units`、`times_in_seconds`、`max_text_width`、`max_user_text`、`text_buffer_size`、`template0-9`、`top_cpu_separate`、`top_name_width`、`extra_newline`、`disable_auto_reload` | ✅ P1 |
| 输出 | `out_to_x`、`out_to_console`、`out_to_stderr`、`out_to_ncurses` | ✅ P1 |
| 图形默认 | `default_bar_size`、`default_gauge_size`、`default_graph_size`、`show_graph_range`、`show_graph_scale` | ✅ P1 |
| 外设 | `hddtemp_host`、`hddtemp_port`、`temperature_unit`、`if_up_strictness`、`mail_spool`、`nvidia_display` | ◐ P2（映射 Windows 传感器） |
| 音乐 | `mpd_host`、`mpd_port`、`mpd_password`、`music_player_interval` | ◐ P2 |
| 文件 | `append_file`、`overwrite_file` | ◐ P2 |
| 邮件 | `imap`、`pop3` | ❌ 解析忽略 |
| 图片 | `imlib_cache_size`、`imlib_cache_flush_interval` | ❌ 解析忽略 |
| Lua | `lua_load`、`lua_draw_hook_pre/post`、`lua_startup/shutdown_hook` | ❌ 解析忽略（non-goal） |
| 其他 | `display`、`max_port_monitor_connections`、`max_specials`、`max_user_text` | ◐ 解析忽略 |

#### 3.4.2 文本布局对象（命令行风格核心）

`${alignc}`、`${alignr N}`、`${goto N}`、`${offset N}`、`${voffset N}`、`${tab N}`、`${hr}`、`${newline}`、`${font}`、`${color}` —— 控制文本对齐、缩进、换行与分隔线，实现纯文本"命令行式"排版。

#### 3.4.3 Windows 不可用对象

Linux 专属对象（`acpi*`、`apm*`、`apcupsd*`、`battery`、`hddtemp`、`platform`、`i2c`、`smapi`、`mpd_*`、`xmms2`、`audacious`、`imap*`、`pop3*`、`rss`、`weather`、`curl`、`stock`、`image`、`nvidia`、`xkb` 等）：**语法完整解析，运行时返回空/占位**（FR-VAR-2），绝不报错——保证任意 Linux 配置可原样加载。

### 3.5 热重载（FR-RELOAD）

**FR-RELOAD-1**：监听配置文件变化，保存后 1s 内自动重载（布局、样式、文本全部更新）。
**FR-RELOAD-2**：重载失败时保留旧配置运行并输出错误日志。

### 3.6 设置界面（FR-SET）

> 提供 GUI 设置面板，降低配置门槛；托盘图标右键菜单中的「设置」入口打开。设计稿见 Pixso「Settings」帧。

**入口与形态（P2 随托盘一并交付，P1 可先行）**：

| 需求 | 说明 | 优先级 |
|---|---|---|
| 设置窗口 | 独立常规窗口（非透明），720×520 左右，可缩放 | P1 |
| 打开方式 | 托盘右键 →「设置」；另有「编辑配置」直接打开 conky.conf | P2 |

**分区与设置项**：

| 分区 | 设置项 | 说明 |
|---|---|---|
| 常规 | 刷新间隔（秒） | 数字输入，默认 2 |
| 常规 | 点击穿透 | 开关，默认开 |
| 常规 | 开机自启 | 开关，默认关 |
| 常规 | 显示器 | 下拉选择，默认主显示器 |
| 配置 | conky.conf 编辑器 | 完整编辑 `conky.config`（alignment / gap / font / default_color / color0-9 等）与 `conky.text`（`${var}` + `${color}` 行内颜色）；**外观全部配置化，不设可视化控件** |
| 关于 | 版本号、开源许可、配置文件路径 | 只读信息 + Badge |

**FR-SET-1**：设置保存后写回 `conky.conf`（`deskmeter` 扩展块 + 受支持的标准键），并立即触发热重载（FR-RELOAD-1）。
**FR-SET-2**：无效输入（如刷新间隔 ≤ 0）在保存时校验提示，不写坏配置。
**FR-SET-3**：提供「恢复默认」按钮，重置为内置默认配置。
**FR-SET-4**：窗口支持「取消」（不保存关闭）与「保存」（写回+重载+关闭）。
**FR-SET-5**：**内容自由度优先（Conky 哲学）**——小部件显示内容 100% 由 `conky.text` 段驱动，用户可自由编写任意行/任意变量/任意顺序/任意颜色组合；设置界面不预设固定显示项清单。设置界面提供：
- `conky.conf` 多行编辑器（**文件编辑器形态**：行号、语法高亮、垂直滚动条、变量插入提示（下拉选变量）、等宽排版）
- 编辑器与文件内容双向同步（保存写回 `conky.conf`，热重载生效）
- **桌面即预览**：小部件常驻桌面，保存设置即热重载即时更新（FR-RELOAD-1），设置窗口内不设嵌入式预览
**FR-SET-6**：**外观全部配置化**——对齐（`alignment`）、边距（`gap_x`/`gap_y`）、字体字号（`font`）、颜色均只通过 `conky.config` 定义，设置界面不提供对齐选择器/边距输入/字体下拉/色板等可视化控件。颜色体系：
- `default_color`：默认文字色
- `color0` ~ `color9`：用户自定义调色板（10 个）
- TEXT 中每行/每段用 `${color}`（恢复默认）、`${color N}`（N=0-9）、`${color #RRGGBB}`（临时色）指定颜色，实现"每行一个颜色 / 每段指定颜色"

### 3.7 扩展与后续（FR-LATER）

| 需求 | 说明 | 优先级 |
|---|---|---|
| `${exec}` | 运行任意命令显示输出（含 stdout 捕获、超时 3s、失败占位） | P1 |
| 温度监控 | LibreHardwareMonitor 集成，兼容 `${platform coretemp.0 temp 1}` / `${hddtemp /dev/sda}` 语法（映射到 Windows 传感器） | P2 |
| Top 进程 | `${top name 1}` `${top cpu 1}` `${top_mem name 1}` `${top_mem mem 1}`（Conky 标准语法） | P2 |
| 音乐控制 | `${mpc title}` / `${mpc artist}`（MPD）；Spotify 等可用 `${exec playerctl ...}` | P2 |
| 托盘图标 | 常驻托盘，右键菜单（刷新、设置、编辑配置、退出） | P2 |
| 开机自启 | 注册启动项 | P2 |
| 多配置管理 | 导入多个 conky.conf、命名、切换当前配置（替代原"主题"方案，用户决策取消主题） | P2 |
| Linux | 同配置格式，/proc + lm-sensors 数据源 | Future |

---

## 4. 非功能需求（NFR）

| 编号 | 指标 | 目标 |
|---|---|---|
| NFR-1 | 空闲 CPU 占用 | < 1%（默认 2s 刷新） |
| NFR-2 | 内存占用 | < 100 MB（**已达成**：私有内存 ~90-95MB；GCHeapHardLimit=32MB + 进程枚举泄漏修复 + 渲染/网络缓存，见 SESSION_SUMMARY 第 7 条） |
| NFR-3 | 启动时间 | < 1s 显示首帧 |
| NFR-4 | 进程数 | 单进程，无子进程常驻 |
| NFR-5 | 网络/遥测 | 无任何网络上报；无广告 |
| NFR-6 | 崩溃恢复 | 配置错误不导致进程退出（见 FR-CFG-3） |

---

## 5. 技术架构

> 技术栈（已确认，2025）：**C# / .NET 8 + WPF**（仅 Windows）· **MoonSharp**（Lua 配置执行）· 许可证 **MIT**。
>
> **目标：Windows 可监测范围内与 Conky 功能无差异**——配置语法、变量对象、行为与 Conky 完全兼容；Conky 源码（`conky-main/`）仅作行为/语法参考，**不复制**（GPL-3.0 传染规避）。
>
> 对照 Conky 源码确认的四个同构机制（均已在下文落地）：① Lua 解释器执行配置（lua-config）② 对象树 + 回调（text_object / OBJ 宏）③ 异步周期回调（update-cb）④ 多后端渲染抽象（display_output_base）。

### 5.1 分层（与 Conky 逐层对应）

```
┌──────────────────────────────────────────────────────┐
│  Display Output（渲染后端抽象 ≈ display_output_base） │
│    WpfWindow（透明置底桌面窗口）· Console · File · HTTP│
├──────────────────────────────────────────────────────┤
│  Renderer（渲染 ≈ x11 + Cairo）                      │
│    等宽字体文本 · Bar/Graph 矢量绘制（DrawingVisual） │
├──────────────────────────────────────────────────────┤
│  Object Tree（文本对象树 ≈ text_object 链表）        │
│    每个 ${var}/文本片段一个节点：print/iftest/barval/ │
│    graphval/percentage 回调 · ifblock 条件跳转        │
│    OBJ / OBJ_ARG / OBJ_IF 注册表（Conky 同款宏模式）  │
├──────────────────────────────────────────────────────┤
│  Data Providers（指标采集 ≈ data/ + data/os/）       │
│    CPU/Mem/Disk/Net/Proc/Exec/Top/Temp + 异步 update_cb│
│    （Task 周期回调，不阻塞主循环）                    │
├──────────────────────────────────────────────────────┤
│  Config Engine（配置引擎 ≈ lua-config）              │
│    MoonSharp 执行 conky.conf（完整 Lua）→ Setting 注册│
│    表 · TEXT 段 → Object Tree 解析                    │
└──────────────────────────────────────────────────────┘
```

### 5.2 关键实现点

| 关注点 | 方案 |
|---|---|
| 配置执行 | **MoonSharp（纯 C# Lua 5.2）**——与 Conky lua-config 同构：`conky.conf` 即完整 Lua 代码，支持注释/函数/变量/计算/`dofile()` 模块化 |
| 对象系统 | **ObjectNode 节点 + 回调委托**（`Print/Iftest/BarVal/GraphVal/Percentage/Free`），`OBJ/OBJ_ARG/OBJ_IF` 注册表（Conky 同款宏模式）；支持条件块（ifblock 跳转）与嵌套解析 |
| 异步采集 | **update_cb 等价物**：`Task` + 周期调度器（`exec`/`top`/网络不阻塞主循环；Conky update-cb.hh 同构：周期 + 剩余计数 + 独立线程） |
| 透明窗口 | `AllowsTransparency` + `WindowStyle=None` + `WS_EX_TRANSPARENT` 点击穿透 |
| 置底 | `SetWindowPos(HWND_BOTTOM)` + 不抢焦点（`WS_EX_NOACTIVATE`） |
| 采集 | `PerformanceCounter`（CPU）/`GlobalMemoryStatusEx`（内存）/`GetDiskFreeSpaceEx`（磁盘）/`GetIfTable`（网络）/LibreHardwareMonitor（温度，P2） |
| 渲染 | WPF `DrawingVisual`：等宽字体文本 + Bar/Graph 矢量绘制（等价 Cairo）；默认等宽字体 `Consolas`/`Cascadia Mono`（中文 fallback Noto Sans SC） |
| 颜色 | `#RRGGBB` + `color0-9` + **X11 命名颜色表**（等价 Conky colours.cc + color-names.yml，如 `grey`/`lightgrey`） |
| 鼠标事件 | 配置驱动点击命令 + **点击小部件弹出设置窗口**（Conky mouse-events 等价物）；与点击穿透互斥，由 `own_window_type` 决定 | P2 |
| 热重载 | `FileSystemWatcher` + 防抖 300ms（`disable_auto_reload` 可关） |
| 单实例 | Mutex 保证单进程 |

### 5.3 建议目录结构

```
DeskMeter/
├─ DeskMeter.sln
├─ src/DeskMeter.App/          # 入口、Display Output 后端（WPF 窗口）
├─ src/DeskMeter.Core/         # Config Engine（MoonSharp）、Object Tree、Data Providers
├─ src/DeskMeter.Render/       # 文本/Bar/Graph 渲染控件（DrawingVisual）
├─ tests/DeskMeter.Tests/      # 单元测试（配置解析、对象渲染、数据源）
├─ samples/conky.conf          # 示例配置（Conky 官方默认配置改写版）
└─ docs/
```

---

## 6. UI 设计规范（与 Pixso 画布对应）

> 设计稿见 Pixso「DeskMeter」页面。以下为设计稿遵循的规范。

| 项 | 规范 |
|---|---|
| 小部件宽度 | 220–320px（文本自适应，推荐 260） |
| 间距刻度 | 4 / 8 / 12 / 16 / 24 |
| 行间距 | 4px（紧凑）、8px（常规）两档 |
| 字体 | 默认等宽/系统字体；中文场景 `Noto Sans SC` |
| 文字颜色 | 由配置控制；默认浅色文字（适配深色壁纸） |
| 语义色 | CPU=主色、内存=主色、磁盘=主色、网络=主色；告警（>80%）=warning、错误（>95%）=error |
| Bar 高度 | 6–8px，圆角 3px，轨道 12–16% 不透明度 |
| Graph 高度 | 28–36px，面积渐变填充 |
| 对齐 | 四角预设 + `gap_x/gap_y`（默认 16px） |
| 透明度 | 背景 0%（纯透明）；仅文字/图形不透明 |

### 设计稿内容（Pixso 画布规划）

| 帧 | 内容 |
|---|---|
| **Desktop Scene（桌面场景）** | 1920×1080 深色壁纸模拟，右上角（+16, +16）放置小部件实例，展示"融入壁纸"效果 |
| **Widget Default（默认小部件）** | 透明小部件：主机名+时钟、CPU 行、内存行、磁盘行、网络上下行、运行时间、CPU 曲线图 |
| **Widget Variants（变体）** | （后续）紧凑布局 / 深色文字版（适配浅色壁纸）/ 不同对齐示例 |

---

## 7. 路线图细化

| 里程碑 | 任务 | 验收 |
|---|---|---|
| **P0** ✅（dev 分支，2025 完成） | ① 配置引擎（MoonSharp Lua 解析 conky.conf）② 透明置底窗口 ③ 对象树基础文本渲染 ④ 定时刷新 | FR-VAR-1/2/3、FR-WIN-1/2、FR-CFG-1/2/3 全过 |
| **P1** | ① Bar/Graph 矢量控件（参数化语法）② 颜色（#hex / colorN / 命名颜色）③ `${exec}` / `${execpi}` 异步 ④ 热重载 ⑤ 布局对象（alignr / goto / hr / scroll） | FR-VIZ-1/2、FR-RELOAD-1/2、FR-LATER.exec |
| **P2** | ① 温度（LibreHardwareMonitor）② Top 进程 ③ 鼠标事件 ④ 托盘 ⑤ 自启 ⑥ 多显示器 ⑦ 多配置管理（用户决策：取消主题，改为导入/命名/切换多份配置）⑧ 设置界面（GUI） | FR-LATER 全过、FR-SET 全过 |
| **Future** | 跨平台（暂不计划；渲染后端已抽象，如需 Linux 可迁移 Avalonia） | — |

---

## 8. 验收标准（摘要）

- [ ] 在任意 Windows 10/11 上：双击启动 → 1s 内桌面出现透明小部件 → 2s 更新一次
- [ ] 鼠标点击小部件区域，事件穿透到下层窗口/桌面
- [ ] 直接加载 **Conky 官方默认配置**（`data/conky.conf`）可正常渲染：可监测对象有值、不可监测对象占位不报错
- [ ] 提供 `samples/conky.conf`，内含 README 所述全部功能示例
- [ ] 设置界面：修改刷新间隔/对齐/显示项并保存，小部件即时生效；非法输入有校验提示
- [ ] 修改配置文件任意文字 → 保存 → 1s 内桌面实时更新
- [ ] 连续运行 24h：内存稳定、无泄漏、无崩溃
- [ ] 拔网线/休眠唤醒等边界场景不崩溃、数据恢复正常

---

## 9. 开放问题（待决策）

| # | 问题 | 选项 |
|---|---|---|
| 1 | 技术栈确认 | C#/WPF（建议） vs Rust+egui vs Tauri |
| 2 | 配置扩展语法 | 独立 `deskmeter` 块（建议） vs 复用 conky 未知键 |
| 3 | 默认 UI 文案语言 | 中文（用户主语言） vs 英文 vs 跟随系统 |
| 4 | 曲线图数据来源 | 自采历史环形缓冲（建议） vs 依赖系统计数器 |
| 5 | 网络接口选择 | 默认活动接口自动探测 vs 配置指定 |
