namespace DeskMeter.Core.Data;

/// <summary>一个进程的 Top 榜条目（Conky top/top_mem 数据）。</summary>
public sealed class ProcessInfo
{
    public ProcessInfo(string name, int pid, double cpuPercent, double memPercent, double cpuSeconds = 0)
    {
        Name = name;
        Pid = pid;
        CpuPercent = cpuPercent;
        MemPercent = memPercent;
        CpuSeconds = cpuSeconds;
    }

    /// <summary>进程名（如 firefox）。</summary>
    public string Name { get; }

    public int Pid { get; }

    /// <summary>CPU 占用 %（两次采样增量计算）。</summary>
    public double CpuPercent { get; }

    /// <summary>内存占用 %（相对物理内存总量）。</summary>
    public double MemPercent { get; }

    /// <summary>累计 CPU 时间（秒，\${top time N} 用）。</summary>
    public double CpuSeconds { get; }
}

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

    /// <summary>CPU 占用榜（\${top ...}，按 CpuPercent 降序，前 10）。</summary>
    public IReadOnlyList<ProcessInfo> TopCpu { get; init; } = Array.Empty<ProcessInfo>();

    /// <summary>每核 CPU 占用 %（\${cpu N} 用；采集失败为空）。</summary>
    public IReadOnlyList<double> CpuCoresPercent { get; init; } = Array.Empty<double>();

    // ---- 网络信息（addr/gw/iface/nameserver/if_gw 等，采集器缓存填充）----
    public IReadOnlyList<string> InterfaceIps { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> GatewayIps { get; init; } = Array.Empty<string>();
    public IReadOnlyList<string> DnsServers { get; init; } = Array.Empty<string>();
    public string DefaultInterfaceName { get; init; } = string.Empty;

    // ---- 平均负载（loadavg：1/5/15 分钟 CPU 占用均值，采集器维护历史）----
    public double LoadAvg1 { get; init; }
    public double LoadAvg5 { get; init; }
    public double LoadAvg15 { get; init; }

    /// <summary>CPU 温度传感器（摄氏，\${platform coretemp.0 temp N} 用）。</summary>
    public IReadOnlyList<double> CpuTemps { get; private set; } = Array.Empty<double>();

    /// <summary>GPU 温度传感器（摄氏）。</summary>
    public IReadOnlyList<double> GpuTemps { get; private set; } = Array.Empty<double>();

    /// <summary>磁盘温度传感器（摄氏，\${hddtemp ...} 用）。</summary>
    public IReadOnlyList<double> DiskTemps { get; private set; } = Array.Empty<double>();

    /// <summary>内存占用榜（\${top_mem ...}，按 MemPercent 降序，前 10）。</summary>
    public IReadOnlyList<ProcessInfo> TopMem { get; init; } = Array.Empty<ProcessInfo>();

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

    /// <summary>设置温度传感器列表（不可变快照内更新用）。</summary>
    public void SetTemperatures(IReadOnlyList<double> cpu, IReadOnlyList<double> gpu, IReadOnlyList<double> disk)
    {
        CpuTemps = cpu.ToList();
        GpuTemps = gpu.ToList();
        DiskTemps = disk.ToList();
    }

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
