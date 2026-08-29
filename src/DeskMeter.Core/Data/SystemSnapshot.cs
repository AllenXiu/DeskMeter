namespace DeskMeter.Core.Data;

/// <summary>一块磁盘的信息。</summary>
public sealed class DiskInfo
{
    public double Used { get; init; }
    public double Free { get; init; }
    public double Total { get; init; }

    public double FreePercent => Total > 0 ? Free / Total * 100.0 : 0;
}

/// <summary>
/// 一次刷新周期的系统指标快照。字段不可监测时保持 0/空，由变量层决定是否显示占位。
/// </summary>
public sealed class SystemSnapshot
{
    public double CpuPercent { get; init; }

    public double MemUsedBytes { get; init; }
    public double MemTotalBytes { get; init; }
    public double MemPercent => MemTotalBytes > 0 ? MemUsedBytes / MemTotalBytes * 100.0 : 0;

    public double SwapUsedBytes { get; init; }
    public double SwapTotalBytes { get; init; }
    public double SwapPercent => SwapTotalBytes > 0 ? SwapUsedBytes / SwapTotalBytes * 100.0 : 0;

    public double CpuFrequencyMhz { get; init; }

    public int ProcessCount { get; init; }
    public int RunningProcessCount { get; init; }

    public long TotalUpBytes { get; init; }
    public long TotalDownBytes { get; init; }
    public double UpSpeedBytesPerSec { get; init; }
    public double DownSpeedBytesPerSec { get; init; }

    public DateTime Now { get; init; } = DateTime.Now;
    public TimeSpan Uptime { get; init; }

    public string HostName { get; init; } = string.Empty;
    public string OsName { get; init; } = string.Empty;
    public string KernelVersion { get; init; } = string.Empty;
    public string Machine { get; init; } = string.Empty;

    private readonly Dictionary<string, DiskInfo> _disks = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>按路径取磁盘信息（/ 与 C:\ 归一化），未采集到时返回空盘。</summary>
    public DiskInfo GetDisk(string path)
    {
        var key = NormalizeDiskPath(path);
        return _disks.TryGetValue(key, out var info) ? info : new DiskInfo();
    }

    public void SetDisk(string path, DiskInfo info) => _disks[NormalizeDiskPath(path)] = info;

    /// <summary>把 Conky 风格路径（/ 或 C:）归一化为 Windows 盘根目录（C:\）。</summary>
    public static string NormalizeDiskPath(string? path)
    {
        if (string.IsNullOrWhiteSpace(path)) return "/";
        var p = path.Trim();
        if (p is "/" or "\\") return Path.GetPathRoot(Environment.SystemDirectory) ?? "/";
        if (p.Length == 2 && p[1] == ':') return p.ToUpperInvariant() + "\\";
        if (p.Length == 3 && p[1] == ':' && (p[2] == '\\' || p[2] == '/')) return p[..2].ToUpperInvariant() + "\\";
        return p;
    }
}
