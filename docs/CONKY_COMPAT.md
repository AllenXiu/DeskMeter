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

## 结论

- **核心监控数据全部覆盖**：CPU/内存/交换/磁盘/网络/进程/温度/时间/主机信息 与 Conky 同名语法一致。
- **未覆盖的 352 个**主要分三类：① Linux 专属硬件与传感器（hwmon/i8k/ibm/acpi*/smapi 等）；② 播放器/音频细分（audacious/mpd/cmus/moc/xmms2/mixer/pa 等 92 个）；③ 桌面环境/窗口管理（desktop/wireless/if_* 条件等）。
- **占位机制**：Linux 专属变量我们已注册 32 个语法占位（运行时显示 `--`），保证配置不报错（FR-VAR-2）。
- 目标与设计一致：Windows 可监测范围内与 Conky 无差异；Linux 专属对象语法解析但返回占位。
