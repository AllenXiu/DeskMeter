using DeskMeter.Core.Config;
using DeskMeter.Core.Data;
using DeskMeter.Core.Text;

namespace DeskMeter.Core.Objects;

/// <summary>
/// 变量求值器：变量名 args → 文本（大小写不敏感）。
/// 返回 null 表示不可监测/未实现 → 显示占位（FR-VAR-2）。
/// </summary>
public static class VariableEvaluator
{
    public const string Version = "DeskMeter 0.1.0";

    /// <summary>Conky spaced_print 宽度：人类可读字节 7、百分比 3。</summary>
    private const int HumanWidth = 7;
    private const int PercentWidth = 3;

    public static string? Evaluate(string name, string[] args, SystemSnapshot data, ConfigSettings settings)
    {
        var key = name.ToLowerInvariant();
        var a = args ?? Array.Empty<string>();
        string Arg(int i) => i < a.Length ? a[i] : string.Empty;
        string Human(double bytes) => Spacer(HumanBytes.Format(bytes), settings, HumanWidth);
        string Percent(double p) => Spacer(FormatPercent(p), settings, PercentWidth);

        switch (key)
        {
            case "hostname": case "nodename": return data.HostName;
            case "sysname": return data.OsName;
            case "kernel": return data.KernelVersion;
            case "machine": return data.Machine;
            case "conky_version": return Version;
            case "uptime": return HumanTime.FormatUptime(data.Uptime);

            case "time": return Strftime.Format(string.IsNullOrEmpty(Arg(0)) ? "%H:%M:%S" : string.Join(" ", a), data.Now);
            case "date": return Strftime.Format(string.IsNullOrEmpty(Arg(0)) ? "%Y-%m-%d" : string.Join(" ", a), data.Now);

            case "cpu":
            {
                // $cpu N：单核占用（PerformanceCounter 每核，采集失败回退总占用）
                var n = ParseInt(Arg(0), -1);
                if (n >= 1 && n <= data.CpuCoresPercent.Count)
                    return Percent(data.CpuCoresPercent[n - 1]);
                return Percent(data.CpuPercent);
            }
            case "cpubar": return null; // 由 BarNode 处理；此处兜底
            case "cpugraph": return null;

            case "mem": return Human(data.MemUsedBytes);
            case "memmax": return Human(data.MemTotalBytes);
            case "memperc": return Percent(data.MemPercent);
            case "membar": return null;

            case "swap": return Human(data.SwapUsedBytes);
            case "swapmax": return Human(data.SwapTotalBytes);
            case "swapperc": return Percent(data.SwapPercent);
            case "swapbar": return null;

            case "fs_used": return Human(data.GetDisk(Arg(0)).Used);
            case "fs_free": return Human(data.GetDisk(Arg(0)).Free);
            case "fs_size": return Human(data.GetDisk(Arg(0)).Total);
            case "fs_free_perc": return Percent(data.GetDisk(Arg(0)).FreePercent);
            case "fs_used_perc": return Percent(100 - data.GetDisk(Arg(0)).FreePercent);
            case "fs_bar": return null;
            case "fs_type": return "NTFS";

            case "downspeed": return Human(data.DownSpeedBytesPerSec) + "/s";
            case "upspeed": return Human(data.UpSpeedBytesPerSec) + "/s";
            case "downspeedf": return data.DownSpeedBytesPerSec.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            case "upspeedf": return data.UpSpeedBytesPerSec.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            case "totaldown": return Human(data.TotalDownBytes);
            case "totalup": return Human(data.TotalUpBytes);

            case "processes": return data.ProcessCount.ToString();
            case "running_processes": return data.RunningProcessCount.ToString();

            case "freq":
            {
                var mhz = data.CpuFrequencyMhz;
                return mhz > 0 ? mhz.ToString("0") : null;
            }
            case "freq_g":
            {
                var mhz = data.CpuFrequencyMhz;
                return mhz > 0 ? (mhz / 1000.0).ToString("0.00", System.Globalization.CultureInfo.InvariantCulture) : null;
            }

            case "top":
            case "top_mem":
            {
                // Conky：top 用 CPU 榜，top_mem 用内存榜；字段 name/pid/cpu/mem
                // 对齐格式与 Conky 一致：name 左对齐 top_name_width+1（默认 16）、pid %7i、cpu/mem %6.2f
                var list = key == "top_mem" ? data.TopMem : data.TopCpu;
                var what = Arg(0).ToLowerInvariant();
                var n = ParseInt(Arg(1), 1) - 1;
                if (n < 0 || n >= list.Count) return null;
                var info = list[n];
                var inv = System.Globalization.CultureInfo.InvariantCulture;
                // name 列：Conky 语义 = top_name_width + 1（默认 16），截断+补齐，防止长进程名顶破列对齐
                var nameWidth = (int)settings.GetNumber("top_name_width", 15) + 1;
                var procName = info.Name.Length > nameWidth ? info.Name[..nameWidth] : info.Name;
                return what switch
                {
                    "name" => procName.PadRight(nameWidth),
                    "pid" => info.Pid.ToString(inv).PadLeft(7),
                    "cpu" => info.CpuPercent.ToString("0.00", inv).PadLeft(6),
                    "mem" => info.MemPercent.ToString("0.00", inv).PadLeft(6),
                    "time" => info.CpuSeconds.ToString("0", inv),
                    _ => null,
                };
            }

            // 温度（LibreHardwareMonitor 映射，未检测到返回占位）
            case "platform":
            {
                // Conky 语法：${platform <type>.<id> <field> <arg>}，如 ${platform coretemp.0 temp 1}
                var device = Arg(0).ToLowerInvariant();
                var field = Arg(1).ToLowerInvariant();
                var arg = ParseInt(Arg(2), 1) - 1;
                if (field != "temp") return null;
                IReadOnlyList<double> list;
                if (device.Contains("gpu", StringComparison.Ordinal) ||
                    device.Contains("radeon", StringComparison.Ordinal) ||
                    device.Contains("nvidia", StringComparison.Ordinal))
                    list = data.GpuTemps;
                else if (device.Contains("disk", StringComparison.Ordinal) ||
                         device.Contains("hdd", StringComparison.Ordinal) ||
                         device.Contains("sda", StringComparison.Ordinal))
                    list = data.DiskTemps;
                else list = data.CpuTemps;
                return arg >= 0 && arg < list.Count
                    ? list[arg].ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                    : null;
            }
            case "hddtemp":
            {
                // Conky 语法：${hddtemp /dev/sda} → 映射到第一个磁盘温度传感器
                return data.DiskTemps.Count > 0
                    ? data.DiskTemps[0].ToString("0", System.Globalization.CultureInfo.InvariantCulture)
                    : null;
            }

            // P1：exec 异步执行；P0 占位
            case "exec": case "execpi": return null;

            // Linux 专属对象：语法完整解析，运行时占位（FR-VAR-2 / §3.4.3）
            case "acpi": case "acpitemp": case "apm_adapter": case "apm_battery": case "apcupsd":
            case "battery": case "battery_time": case "battery_percent": case "battery_short":
            case "i2c": case "smapi":
            case "mpd_artist": case "mpd_title": case "mpd_album": case "mpd_vol": case "mpd_random":
            case "mpc": case "xmms2_artist": case "audacious_title":
            case "imap_unseen": case "imap_messages": case "pop3_unseen": case "pop3_messages":
            case "rss": case "weather": case "curl": case "stock": case "image": case "nvidia": case "xkb":
                return null;

            default:
                return null;
        }
    }

    private static int ParseInt(string s, int fallback)
        => int.TryParse(s, out var v) ? v : fallback;

    private static string FormatPercent(double p)
    {
        // Conky 语义：$cpu / $memperc 等输出纯数字，% 由配置中的字面量追加
        p = Math.Clamp(p, 0, 100);
        return p.ToString("0", System.Globalization.CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// use_spacer（Conky spaced_print）：left = 右侧补齐（右对齐），right = 左侧补齐（左对齐）。
    /// none 时原样输出。用于固定可变宽字段，防止刷新时布局/窗口宽度抖动。
    /// </summary>
    private static string Spacer(string value, ConfigSettings settings, int width)
    {
        switch (settings.GetUseSpacer())
        {
            case "left":
                return value.Length < width ? value.PadLeft(width) : value;
            case "right":
                return value.Length < width ? value.PadRight(width) : value;
            default:
                return value;
        }
    }
}
