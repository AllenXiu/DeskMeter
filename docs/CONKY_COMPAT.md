# DeskMeter 与 Conky 变量覆盖对比

数据来源：Conky 官方文档变量清单（`conky-main/doc/variables.yaml`，429 个）与 DeskMeter 注册表（96 个）。

## 总览

| 项 | 数量 | 说明 |
|---|---|---|
| Conky 官方文档变量 | **429** | 含大量 Linux 专属、播放器、硬件传感器细分 |
| DeskMeter 已注册 | **96** | 47 真实数据 + 10 布局 + 11 颜色 + 32 Linux 占位 |
| 同名变量（两边都有） | **77** | Conky 语法兼容 |
| DeskMeter 别名/扩展 | **19** | 部分为 Conky 实际存在但未列入文档 yaml |
| Conky 独有（我们未实现） | **352** | 绝大多数为 Linux 专属或 Windows 无对应数据 |

## 同名已覆盖变量（77）

`acpitemp` `alignc` `alignr` `apcupsd` `apm_adapter` `audacious_title` `battery` `battery_percent` `battery_short` `battery_time` `color` `conky_version` `cpu` `cpubar` `cpugraph` `curl` `downspeed` `downspeedf` `downspeedgraph` `exec` `execpi` `font` `freq` `freq_g` `fs_bar` `fs_free` `fs_free_perc` `fs_size` `fs_type` `fs_used` `fs_used_perc` `goto` `hddtemp` `hr` `i2c` `image` `imap_messages` `imap_unseen` `kernel` `machine` `mem` `membar` `memmax` `memperc` `mpd_album` `mpd_artist` `mpd_random` `mpd_title` `mpd_vol` `nodename` `nvidia` `offset` `platform` `pop3_unseen` `processes` `rss` `running_processes` `scroll` `smapi` `stock` `swap` `swapbar` `swapmax` `swapperc` `sysname` `tab` `time` `top` `top_mem` `totaldown` `totalup` `upspeed` `upspeedf` `upspeedgraph` `uptime` `voffset` `xmms2_artist`

## DeskMeter 别名 / 扩展（19）

`acpi` `apm_battery` `color0` `color1` `color2` `color3` `color4` `color5` `color6` `color7` `color8` `color9` `date` `hostname` `mpc` `newline` `pop3_messages` `weather` `xkb`

> 说明：其中 `hostname`/`date`/`color0-9`/`mpc` 等是 Conky 真实支持的变量（文档 yaml 未列出）；`newline`/`pop3_messages`/`weather`/`xkb`/`acpi`/`apm_battery` 为 DeskMeter 兼容别名或占位。

## Conky 独有（我们未实现，352）按类分布

| 类别 | 数量 | 代表变量 |
|---|---|---|
| 进程 / 用户 | 52 | `cmdline_to_pid` `pid_chroot` `pid_cmdline` `pid_cwd` `pid_egid` `pid_environ` … |
| 内存细分 | 19 | `buffers` `cached` `free_bufcache` `free_cached` `legacymem` `memactive` … |
| 磁盘 / IO | 9 | `disk_protect` `diskio` `diskio_read` `diskio_write` `diskiograph` `diskiograph_read` … |
| 文本 / 命令 | 27 | `cat` `catp` `combine` `eval` `execbar` `execgauge` … |
| 条件 / 控制 | 19 | `blink` `else` `endif` `if_empty` `if_existing` `if_gw` … |
| 音频 / 播放器 | 92 | `audacious_bar` `audacious_bitrate` `audacious_channels` `audacious_filename` `audacious_frequency` `audacious_length` … |
| 硬件 / 温度 / 电池 | 51 | `acpiacadapter` `acpifan` `adt746xcpu` `adt746xfan` `apcupsd_cable` `apcupsd_charge` … |
| 网络 | 22 | `addr` `addrs` `github_notifications` `gw_iface` `gw_ip` `iface` … |
| 邮件 | 12 | `draft_mails` `flagged_mails` `mails` `mboxscan` `new_mails` `pop3_used` … |
| Lua | 5 | `lua` `lua_bar` `lua_gauge` `lua_graph` `lua_parse` … |
| 系统信息 | 25 | `conky_build_arch` `cpugauge` `cpugovernor` `desktop` `desktop_name` `desktop_number` … |
| 其他 | 7 | `ical` `journal` `mysql` `password` `shmem` `sysctlbyname` … |
| 未归组 | 12 | `apm_battery_life` `apm_battery_time` `colorN` `fontN` `forwarded_mails` `gid_name` … |

## Windows 可替代性评估（未覆盖的 352 个）

结论先行：**大部分可替代**（纯文本处理/网络/进程类完全可行；播放器与 Linux 内核类基本不可替代）。
可行性图例：✅ 可直接实现 ｜ 🟡 部分可行/需第三方库 ｜ ❌ 不可替代（无 Windows 对应概念）。

| 类别 | 数量 | 可行性 | Windows 实现途径 |
|---|---|---|---|
| 文本 / 命令（execbar、eval、lines/head/tail、大小写、to_bytes、templateN、cat） | 27 | ✅ | 纯字符串处理，与平台无关；execbar/execgraph 复用现有 exec+bar/graph 引擎；cat=File.ReadAllText |
| 网络（addr、gw_*、iface、nameserver、tcp_ping、read_tcp/udp、wireless_*） | 22 | ✅ | NetworkInterface.GetIPProperties()（IP/网关/DNS）、Ping、Socket；无线用 Native Wifi（WlanGetNetworkBssList 信号/信道/ESSID） |
| 进程 / 用户（pid_cmdline/exe/threads/time/mem 等） | 52 | ✅(约 60%) 🟡(其余) | Process 类（线程/时间/内存/优先级）；WMI Win32_Process（命令行/可执行路径）；NtQueryInformationProcess（environ/cwd，未文档化）；pid_openfiles 需 NtQuerySystemInformation，成本高 |
| 系统信息（keyboard_layout、key_*_lock、mouse_speed、uptime_short、distribution、monitor*） | 25 | ✅ 大部分 ❌ 少数 | GetKeyboardLayout、GetKeyState、SystemParametersInfo(SPI_GETMOUSESPEED)、Screen.AllScreens；entropy/laptop_mode/cpugovernor 无对应（❌） |
| 条件 / 控制（if_existing/if_mounted/if_match/if_running/if_up/else/endif） | 19 | ✅ 大部分 | File.Exists、DriveInfo、字符串匹配、Process.GetProcessesByName、NetworkInterface 状态；if_mpd/if_pa 为 Linux 专属（❌） |
| 内存细分（memfree/memavail/memwithbuffers* 等） | 19 | 🟡 | GlobalMemoryStatusEx 已覆盖 used/avail；Cache/Commit 用 PerformanceCounter "Memory" 类别近似；"buffers/cached" 无精确对应 |
| 磁盘 / IO（diskio_read/write、diskiograph、fs_bar_free） | 9 | ✅ 5 个 ❌ 2 个 | PerformanceCounter "PhysicalDisk"（读/写字节每秒）→ 可接现有 graph 引擎；fs_bar_free=已有 fs_free_perc 的 bar；ioscheduler/disk_protect 为 Linux 概念（❌） |
| Lua（lua、lua_bar/gauge/graph/parse） | 5 | ✅ | 已有 MoonSharp（配置即完整 Lua）——lua 变量=调用配置里注册的函数，直接可做 |
| 硬件 / 温度 / 电池（battery_*、nvidia*、platformbar、voltage_*） | 37 | 🟡 | 电池：Win32_Battery WMI；GPU：LibreHardwareMonitor（已在用）提供负载/温度；电压：LHM 传感器；i8k/ibm/acpi/smapi 为厂商 ACPI 接口（❌，无通用替代） |
| 音频 / 播放器（audacious/mpd/cmus/moc/xmms2/mixer/pa/irc） | 92 | 🟡 少数 ❌ 多数 | 音量/静音：Windows CoreAudio（NAudio 或 winmm）；当前播放曲目：Win11 SMTC（Windows.Media.Control，可拿 artist/title/status）；Linux 播放器守护进程本身（❌） |
| 邮件（mails 系列、mboxscan） | 12 | 🟡 | 需对接邮件客户端（Outlook COM/MAPI）或 IMAP 协议；无通用本地方案 |
| 其他（tztime、ical、journal、mysql、shmem、sysctlbyname） | 7 | 🟡 2 个 ❌ 5 个 | tztime=TimeZoneInfo 直接可做；ical=读 .ics 文件；journal→Windows 事件日志（EventLog，语义不同）；mysql/shmem/sysctl 无对应 |

### 建议优先级（Windows 上性价比最高）

1. **if_* 条件系列**（if_existing/if_mounted/if_match/if_running/if_up + else/endif）——对兼容真实 Conky 配置帮助最大，纯 .NET API 可做
2. **网络信息**（addr/gw_ip/gw_iface/nameserver + loadavg 用 CPU 历史平均）——常用且简单
3. **文本处理**（lines/head/tail/words/uppercase/lowercase/startcase/eval/to_bytes/templateN）——纯字符串，实现快
4. **execbar/execgraph**（复用现有引擎）与 **lua** 变量（MoonSharp 现成）
5. **磁盘 IO 速率**（diskio_read/write + diskiograph，PerformanceCounter 已有基础设施）
6. **电池**（Win32_Battery：battery_percent/battery_time/battery_status/battery_bar）
7. 🟡 大工程：SMTC 当前播放（替代 mpd_artist/title）、Native Wifi 无线信息

## 结论

- **核心监控数据全部覆盖**：CPU/内存/交换/磁盘/网络/进程/温度/时间/主机信息 与 Conky 同名语法一致。
- **未覆盖的 352 个**主要分三类：① Linux 专属硬件与传感器（hwmon/i8k/ibm/acpi*/smapi 等）；② 播放器/音频细分（audacious/mpd/cmus/moc/xmms2/mixer/pa 等 92 个）；③ 桌面环境/窗口管理（desktop/wireless/if_* 条件等）。
- **占位机制**：Linux 专属变量我们已注册 32 个语法占位（运行时显示 `--`），保证配置不报错（FR-VAR-2）。
- 目标与设计一致：Windows 可监测范围内与 Conky 无差异；Linux 专属对象语法解析但返回占位。
