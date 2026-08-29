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

    public static string? Evaluate(string name, string[] args, SystemSnapshot data, ConfigSettings settings)
    {
        var key = name.ToLowerInvariant();
        var a = args ?? Array.Empty<string>();
        string Arg(int i) => i < a.Length ? a[i] : string.Empty;

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
                // 单核（$cpu N）P1 细化；P0 输出总占用
                return FormatPercent(data.CpuPercent);
            }
            case "cpubar": return null; // 由 BarNode 处理；此处兜底
            case "cpugraph": return null;

            case "mem": return HumanBytes.Format(data.MemUsedBytes);
            case "memmax": return HumanBytes.Format(data.MemTotalBytes);
            case "memperc": return FormatPercent(data.MemPercent);
            case "membar": return null;

            case "swap": return HumanBytes.Format(data.SwapUsedBytes);
            case "swapmax": return HumanBytes.Format(data.SwapTotalBytes);
            case "swapperc": return FormatPercent(data.SwapPercent);
            case "swapbar": return null;

            case "fs_used": return HumanBytes.Format(data.GetDisk(Arg(0)).Used);
            case "fs_free": return HumanBytes.Format(data.GetDisk(Arg(0)).Free);
            case "fs_size": return HumanBytes.Format(data.GetDisk(Arg(0)).Total);
            case "fs_free_perc": return FormatPercent(data.GetDisk(Arg(0)).FreePercent);
            case "fs_used_perc": return FormatPercent(100 - data.GetDisk(Arg(0)).FreePercent);
            case "fs_bar": return null;
            case "fs_type": return "NTFS";

            case "downspeed": return HumanBytes.Format(data.DownSpeedBytesPerSec) + "/s";
            case "upspeed": return HumanBytes.Format(data.UpSpeedBytesPerSec) + "/s";
            case "downspeedf": return data.DownSpeedBytesPerSec.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            case "upspeedf": return data.UpSpeedBytesPerSec.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
            case "totaldown": return HumanBytes.Format(data.TotalDownBytes);
            case "totalup": return HumanBytes.Format(data.TotalUpBytes);

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

            // P2：Top 进程 / 温度 / 音乐
            case "top": case "top_mem": return null;

            // P1：exec 异步执行；P0 占位
            case "exec": case "execpi": return null;

            // Linux 专属对象：语法完整解析，运行时占位（FR-VAR-2 / §3.4.3）
            case "acpi": case "acpitemp": case "apm_adapter": case "apm_battery": case "apcupsd":
            case "battery": case "battery_time": case "battery_percent": case "battery_short":
            case "hddtemp": case "platform": case "i2c": case "smapi":
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
}
