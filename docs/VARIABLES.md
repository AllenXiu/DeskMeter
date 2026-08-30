# DeskMeter 变量/系统数据接口参考

DeskMeter 兼容 Conky 的 `conky.text` 语法：`$name` 与 `${name args}` 两种形式，变量名大小写不敏感。
本文档列出当前版本（v0.1.0）**全部已注册变量（96 个）** 及其在 Windows 上的实现状态。

状态图例：
- ✅ **真实数据** —— 读取 Windows 系统数据
- ⚠️ **部分实现** —— 语法已解析，行为为近似/简化
- 🅿️ **占位** —— 语法完整解析，运行时显示 `--`（Linux 专属对象）

---

## 1. 系统信息（6）

| 变量 | 说明 | 状态 |
|---|---|---|
| `$hostname` / `$nodename` | 计算机名 | ✅ |
| `$sysname` | 系统名称（如 "Windows 11 ..."） | ✅ |
| `$kernel` | 内核版本号（Windows OS 版本） | ✅ |
| `$machine` | 架构（x86_64 / x86） | ✅ |
| `$conky_version` | DeskMeter 版本字符串 | ✅ |

## 2. 时间（3）

| 变量 | 说明 | 状态 |
|---|---|---|
| `$time` / `${time 格式}` | 当前时间，默认 `%H:%M:%S`，支持 strftime 格式 | ✅ |
| `$date` / `${date 格式}` | 当前日期，默认 `%Y-%m-%d` | ✅ |
| `$uptime` | 系统运行时长（如 "1d 21h 49m"） | ✅ |

## 3. CPU（6）

| 变量 | 说明 | 状态 |
|---|---|---|
| `$cpu` | CPU 总占用 % | ✅ |
| `${cpu N}` | 第 N 个核心占用 %（PerformanceCounter 每核，失败回退总占用） | ✅ |
| `${cpubar 高度[,宽度]}` | CPU 占用矢量进度条（宽度省略=填满剩余行宽） | ✅ |
| `${cpugraph [旗标] 高度[,宽度]}` | CPU 曲线图（环形缓冲 80 点；旗标 -l 对数刻度、-m 固定最大值、-i 采样间隔、-y 纵轴倍率；-t/-x 解析忽略） | ✅ |
| `$freq` | CPU 频率 MHz | ✅ |
| `$freq_g` | CPU 频率 GHz（2 位小数） | ✅ |

## 4. 内存 / 交换（9）

| 变量 | 说明 | 状态 |
|---|---|---|
| `$mem` / `$memmax` | 内存已用 / 总量（GiB，带 use_spacer 对齐） | ✅ |
| `$memperc` | 内存占用 % | ✅ |
| `${membar 高度[,宽度]}` | 内存进度条 | ✅ |
| `$swap` / `$swapmax` / `$swapperc` | 交换分区已用 / 总量 / % | ✅ |
| `${swapbar 高度[,宽度]}` | 交换进度条 | ✅ |

## 5. 磁盘 / 文件系统（7，均支持多盘）

| 变量 | 说明 | 状态 |
|---|---|---|
| `${fs_used 路径}` / `${fs_free 路径}` / `${fs_size 路径}` | 指定路径磁盘已用 / 剩余 / 总量 | ✅ |
| `${fs_free_perc 路径}` / `${fs_used_perc 路径}` | 剩余 / 已用 % | ✅ |
| `${fs_bar 高度[,宽度] 路径}` | 磁盘进度条 | ✅ |
| `${fs_type 路径}` | 文件系统类型（Windows 固定 NTFS） | ✅ |

> 路径写法：`/` 或 `\` = 系统盘（Windows 所在盘）；`C:`、`C:/`、`C:\` = 指定盘。
> 采集逻辑枚举**所有就绪盘/分区**（DriveInfo.GetDrives()），可同时显示任意数量硬盘。

## 6. 网络（8）

| 变量 | 说明 | 状态 |
|---|---|---|
| `$downspeed` / `$upspeed` | 当前下载 / 上传速率（GiB/MiB/KiB/B + /s） | ✅ |
| `$downspeedf` / `$upspeedf` | 速率浮点数（B/s） | ✅ |
| `$totaldown` / `$totalup` | 累计下载 / 上传总量 | ✅ |
| `${downspeedgraph [旗标] 高度[,宽度]}` / `${upspeedgraph ...}` | 下载 / 上传速率曲线图（同 cpugraph 旗标） | ✅ |

## 7. 进程（3）

| 变量 | 说明 | 状态 |
|---|---|---|
| `$processes` | 进程总数 | ✅ |
| `$running_processes` | 运行中进程数（含任一 Running 线程） | ✅ |
| `${top 字段 N}` / `${top_mem 字段 N}` | 进程榜：top=CPU 榜，top_mem=内存榜；N 从 1 开始 | ✅ |

> `top` 字段：`name`（左对齐，top_name_width+1=16）、`pid`（%7i）、`cpu` / `mem`（%6.2f）、`time`（CPU 秒）。
> 示例：`${top name 1}` `${top pid 1}` `${top cpu 1}` `${top mem 1}`

## 8. 温度（2）

| 变量 | 说明 | 状态 |
|---|---|---|
| `${platform 设备.编号 temp N}` | 温度传感器：coretemp/amd→CPU、radeon/nvidia/gpu→GPU、disk/hdd/sda→存储；N 为传感器序号（1 起） | ✅ |
| `${hddtemp 设备}` | 磁盘温度（映射到第一个磁盘温度传感器） | ✅ |

> 基于 LibreHardwareMonitor 后台采集（2s）。CPU 温度通常需要管理员权限；无传感器显示 `--`。
> 示例：`${platform coretemp.0 temp 1}`（CPU） `${platform radeon.0 temp 1}`（GPU）

## 9. 命令执行（2）

| 变量 | 说明 | 状态 |
|---|---|---|
| `${exec 命令}` | 执行命令并显示输出（cmd /c，3s 超时，异步不阻塞） | ✅ |
| `${execpi 间隔 命令}` | 周期执行（间隔秒数，首参为间隔） | ✅ |

## 10. 布局 / 文本格式（10）

| 变量 | 说明 | 状态 |
|---|---|---|
| `$hr` | 分隔线（本行） | ✅ |
| `$newline` | 强制换行 | ✅ |
| `${scroll [left|right|wait] 长度 [步长] [间隔] 文本}` | 文本滚动：left 左向（默认）、right 从尾部向右滚入、wait 到尾停留后回绕；step 步长、interval 每 N 次刷新前进 | ✅ |
| `${goto N}` | 水平绝对定位（像素，计入行宽） | ✅ |
| `${alignc}` / `${alignr N}` | 行内居中 / 右对齐 | ✅ |
| `${offset N}` / `${voffset N}` / `${tab N}` | 像素偏移 / 垂直偏移 / 像素制表位（可为负） | ✅ |
| `${font 家族:size=字号}` | 行内切换字体与字号（无参数恢复配置默认） | ✅ |

## 11. 颜色（11）

| 变量 | 说明 | 状态 |
|---|---|---|
| `${color}` | 恢复默认颜色 | ✅ |
| `${color 名称}` | 命名颜色（X11/CSS 约 140 色，grey=190 按 Conky 标准） | ✅ |
| `${color #RRGGBB}` | 十六进制颜色 | ✅ |
| `${color N}` | 调色板第 N 色（color0-9 配置项） | ✅ |
| `$color0` … `$color9` | 调色板独立变量形式 | ✅ |

## 12. Linux 专属占位（32）

语法完整解析、运行时显示 `--`（Windows 无对应数据）：

`acpi` `acpitemp` `apm_adapter` `apm_battery` `apcupsd` `battery` `battery_time` `battery_percent` `battery_short`
`i2c` `smapi` `mpd_artist` `mpd_title` `mpd_album` `mpd_vol` `mpd_random` `mpc`
`xmms2_artist` `audacious_title` `imap_unseen` `imap_messages` `pop3_unseen` `pop3_messages`
`rss` `weather` `curl` `stock` `image` `nvidia` `xkb`

---

## 汇总

| 类别 | 数量 |
|---|---|
| 系统信息 / 时间 / CPU / 内存 / 磁盘 / 网络 / 进程 / 温度 / 执行（真实数据） | 47 |
| 布局与文本格式 | 10 |
| 颜色 | 11 |
| Linux 专属占位 | 32 |
| **合计（已注册变量名）** | **96** |


## 13. Conky 扩展（Windows 替代，已实现）

| 变量 | 说明 | 状态 |
|---|---|---|
| `${if_existing 路径}...${else}...${endif}` | 文件/目录存在则输出 then 分支 | ✅ |
| `${if_mounted 盘符}...${endif}` | 磁盘就绪（`/`→系统盘，`C:`→C 盘） | ✅ |
| `${if_match 表达式}...${endif}` | 比较表达式（== != > < >= <=，数值/字符串） | ✅ |
| `${if_running 进程名}...${endif}` | 进程是否在运行（不含 .exe） | ✅ |
| `${if_up [网卡]}...${endif}` | 指定/任意非回环网卡是否 Up | ✅ |
| `${if_empty 文本}...${endif}` | 参数展开后是否为空 | ✅ |
| `${if_updatenr N}...${endif}` | 当前刷新序号 == N | ✅ |
| `${if_gw IP}...${endif}` | 默认网关 == IP | ✅ |
| `${else}` / `${endif}` | 条件块分支与结束（支持嵌套） | ✅ |
| `$addr` / `$addrs` | 本机 IPv4 地址 / 全部地址 | ✅ |
| `$gw_ip` / `$gw_iface` / `$iface` | 默认网关 IP / 网卡名 | ✅ |
| `$nameserver` | DNS 服务器 | ✅ |
| `$loadavg` | 1/5/15 分钟 CPU 占用均值（Windows 用 CPU 历史近似） | ✅ |
| `$diskio` / `$diskio_read` / `$diskio_write` | 磁盘 IO 总/读/写速率（PhysicalDisk 计数器） | ✅ |
| `${diskiograph [旗标] 高,宽}` 及 `_read` / `_write` | 磁盘 IO 速率曲线图 | ✅ |
| `$battery` / `$battery_percent` | 电池电量 % | ✅ |
| `$battery_time` / `$battery_short` | 剩余时间（H:mm:ss / H:mm） | ✅ |
| `$battery_status` | charging / full / discharging（GetSystemPowerStatus） | ✅ |
| `${battery_bar 高,宽}` | 电池电量进度条 | ✅ |

## 通用行为

- **use_spacer**（`left` / `right` / `none`）：人类可读字节补到 7 字符、百分比补到 3 字符，防止刷新抖动
- **更新间隔**：`update_interval` 配置项（秒），默认 2
- **不可监测**：返回 `--` 占位，不报错（FR-VAR-2）
- **多显示器**：`deskmeter.monitor` 指定目标屏；点击穿透 `deskmeter.click_through`；温度开关 `deskmeter.temperature`