using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace DeskMeter.Core.Data;

/// <summary>
/// macOS 系统数据采集器（DeskMeter for macOS 移植版）。
/// 数据源：libSystem (mach host_statistics / host_processor_info) + sysctl + 轻量系统命令
/// （sw_vers / uname / vm_stat / netstat / pmset / df），全部容错——失败返回 0/空（FR-VAR-2）。
/// v1 范围：CPU（总/每核）、内存/交换、磁盘（挂载点）、网络速率、进程 Top、系统信息。
/// 暂不支持（返回空/0）：温度传感器、每进程磁盘 IO / GPU / 连接数（macOS 无公开 API，后续可选 SMC）。
/// </summary>
public sealed class MacSystemDataCollector : IDisposable
{
    // ---- CPU 采样状态 ----
    private long _prevUser, _prevSystem, _prevIdle, _prevNice;
    private bool _cpuInited;
    private readonly Dictionary<int, (ulong U, ulong S, ulong I, ulong N)> _corePrev = new();
    private readonly List<double> _cpuHistory = new();
    private long _prevNetRecv, _prevNetSent;
    private DateTime _prevNetTime;
    private bool _netInited;
    private string? _defaultIface;
    private readonly Dictionary<int, (TimeSpan Cpu, DateTime Wall)> _procPrev = new();
    private int _driveRescanCounter;
    private IReadOnlyList<(string Path, double Used, double Free, double Total)> _disks = Array.Empty<(string, double, double, double)>();
    private string? _osName, _kernelCache, _freqCache;

    /// <summary>任务管理器式 Top 扩展（macOS v1 仅 cpu/mem/pid/name 有意义）。</summary>
    public string TopSort { get; set; } = "cpu";
    public bool CollectDiskMetrics { get; set; }
    public bool CollectGpuMetrics { get; set; }
    public bool CollectNetMetrics { get; set; }

    public void RequestTemperature() { /* macOS v1 不提供温度 */ }

    public SystemSnapshot Collect()
    {
        var now = DateTime.Now;
        var (cpu, cores) = GetCpu();
        _cpuHistory.Add(cpu);
        if (_cpuHistory.Count > 900) _cpuHistory.RemoveAt(0);

        var (recv, sent) = GetNetTotals();
        var speed = GetNetSpeed(recv, sent, now);
        var (memUsed, memTotal, swapUsed, swapTotal) = GetMemory();
        var procSnap = CollectProcesses(DateTime.UtcNow, memTotal);
        var topActive = BuildTopActive(procSnap.Items);
        var (battPercent, battSeconds, battStatus) = GetBattery();
        EnsureDisks();

        var snap = new SystemSnapshot
        {
            CpuPercent = cpu,
            CpuCoresPercent = cores,
            MemUsedBytes = memUsed,
            MemTotalBytes = memTotal,
            SwapUsedBytes = swapUsed,
            SwapTotalBytes = swapTotal,
            CpuFrequencyMhz = GetCpuFrequencyMhz(),
            ProcessCount = procSnap.Count,
            RunningProcessCount = procSnap.RunningCount,
            TotalUpBytes = sent,
            TotalDownBytes = recv,
            UpSpeedBytesPerSec = speed.up,
            DownSpeedBytesPerSec = speed.down,
            Now = now,
            Uptime = TimeSpan.FromMilliseconds(Environment.TickCount64),
            HostName = Safe(() => Environment.MachineName) ?? string.Empty,
            OsName = GetOsName(),
            KernelVersion = GetKernelVersion(),
            Machine = RuntimeInformation.OSArchitecture == Architecture.Arm64 ? "arm64" : "x86_64",
            TopCpu = procSnap.Items.OrderByDescending(x => x.CpuPercent).Take(10).ToList(),
            TopMem = procSnap.Items.OrderByDescending(x => x.MemPercent).Take(10).ToList(),
            TopActive = topActive,
            LoadAvg1 = AverageHistory(60),
            LoadAvg5 = AverageHistory(300),
            LoadAvg15 = AverageHistory(900),
            InterfaceIps = Safe(GetIps) ?? Array.Empty<string>(),
            GatewayIps = Safe(GetGateways) ?? Array.Empty<string>(),
            DnsServers = Safe(GetDnsServers) ?? Array.Empty<string>(),
            DefaultInterfaceName = _defaultIface ?? string.Empty,
        };
        foreach (var d in _disks) snap.SetDisk(d.Path, new DiskInfo { Used = d.Used, Free = d.Free, Total = d.Total });
        return snap;
    }

    // ---- CPU ----

    private (double total, IReadOnlyList<double> cores) GetCpu()
    {
        try
        {
            var (u, s, i, n) = ReadCpuTicks();
            if (!_cpuInited)
            {
                _cpuInited = true;
                _prevUser = u; _prevSystem = s; _prevIdle = i; _prevNice = n;
                return (0, Array.Empty<double>());
            }
            var du = u - _prevUser; var ds = s - _prevSystem;
            var di = i - _prevIdle; var dn = n - _prevNice;
            _prevUser = u; _prevSystem = s; _prevIdle = i; _prevNice = n;
            var sum = du + ds + di + dn;
            var total = sum > 0 ? (du + ds + dn) / (double)sum * 100.0 : 0;

            // 每核（host_processor_info 每核 user/system/idle/nice tick）
            var cores = new List<double>();
            var perCore = ReadCoreTicks();
            if (perCore.Count > 0)
            {
                for (var idx = 0; idx < perCore.Count; idx++)
                {
                    var c = perCore[idx];
                    if (!_corePrev.TryGetValue(idx, out var prev))
                    {
                        _corePrev[idx] = c;
                        continue;
                    }
                    var du2 = c.U - prev.U; var ds2 = c.S - prev.S;
                    var di2 = c.I - prev.I; var dn2 = c.N - prev.N;
                    var sum2 = du2 + ds2 + di2 + dn2;
                    cores.Add(sum2 > 0 ? (du2 + ds2 + dn2) / (double)sum2 * 100.0 : 0);
                    _corePrev[idx] = c;
                }
            }
            return (total, cores);
        }
        catch
        {
            return (0, Array.Empty<double>());
        }
    }

    private static (long u, long s, long i, long n) ReadCpuTicks()
    {
        var host = mach_host_self();
        var buf = Marshal.AllocHGlobal(4 * 4);
        try
        {
            var count = 4;
            if (host_statistics(host, HOST_CPU_LOAD_INFO, buf, ref count) != 0 || count < 4)
                return (0, 0, 0, 0);
            return (Marshal.ReadInt32(buf, 0), Marshal.ReadInt32(buf, 4),
                    Marshal.ReadInt32(buf, 8), Marshal.ReadInt32(buf, 12));
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static List<(ulong U, ulong S, ulong I, ulong N)> ReadCoreTicks()
    {
        var result = new List<(ulong, ulong, ulong, ulong)>();
        var host = mach_host_self();
        uint cpuCount = 0;
        IntPtr info = IntPtr.Zero;
        var infoCnt = 0;
        try
        {
            if (host_processor_info(host, PROCESSOR_CPU_LOAD_INFO, ref cpuCount, out info, ref infoCnt) != 0 || info == IntPtr.Zero)
                return result;
            for (var i = 0; i < cpuCount; i++)
            {
                var p = IntPtr.Add(info, i * 4 * 4);
                result.Add(((ulong)Marshal.ReadInt32(p, 0), (ulong)Marshal.ReadInt32(p, 4),
                            (ulong)Marshal.ReadInt32(p, 8), (ulong)Marshal.ReadInt32(p, 12)));
            }
        }
        catch { return result; }
        finally
        {
            if (info != IntPtr.Zero)
                try { vm_deallocate(mach_task_self(), info, infoCnt * 4); } catch { }
        }
        return result;
    }

    // ---- 内存 / 交换 ----

    private static (double used, double total, double swapUsed, double swapTotal) GetMemory()
    {
        var page = Safe(() => double.Parse(Shell("sysctl -n hw.pagesize").Trim()));
        var total = Safe(() => double.Parse(Shell("sysctl -n hw.memsize").Trim()));
        if (page <= 0) page = 4096;
        double free = 0, active = 0, inactive = 0, wire = 0, compressor = 0;
        var vm = Shell("vm_stat");
        free = ParseVm("Pages free", vm) * page;
        active = ParseVm("Pages active", vm) * page;
        inactive = ParseVm("Pages inactive", vm) * page;
        wire = ParseVm("Pages wired down", vm) * page;
        compressor = ParseVm("Pages occupied by compressor", vm) * page;
        // macOS 口径：已用 = active + wired + compressor（≈ 活动监视器 Memory Used）
        var used = total > 0 ? Math.Min(active + wire + compressor, total) : active + wire + compressor;
        var (su, st) = ParseSwap();
        return (used, total, su, st);
    }

    private static double ParseVm(string key, string vm)
    {
        var m = Regex.Match(vm, key + @":\s+([0-9]+)");
        return m.Success ? double.Parse(m.Groups[1].Value) : 0;
    }

    private static (double used, double total) ParseSwap()
    {
        // vm.swapusage: "total = 4096.00M  used = 0.00M  available = 3072.00M  capacity = 75.00M"
        var s = Shell("sysctl -n vm.swapusage");
        var m = Regex.Match(s, @"total\s*=\s*([0-9.]+)M\s+used\s*=\s*([0-9.]+)M");
        if (!m.Success) return (0, 0);
        const double meg = 1024.0 * 1024.0;
        return (double.Parse(m.Groups[2].Value) * meg, double.Parse(m.Groups[1].Value) * meg);
    }

    // ---- 磁盘（df -k 解析挂载点，60 tick 重扫）----

    private void EnsureDisks()
    {
        if (_disks.Count > 0 && ++_driveRescanCounter < 60) return;
        _driveRescanCounter = 0;
        var list = new List<(string, double, double, double)>();
        try
        {
            var df = Shell("df -kP");
            foreach (var line in df.Split('\n').Skip(1))
            {
                var parts = Regex.Split(line.Trim(), @"\s+");
                if (parts.Length < 6) continue;
                if (!double.TryParse(parts[1], out var blocks) || !double.TryParse(parts[2], out var usedK)
                    || !double.TryParse(parts[3], out var freeK) || blocks <= 0) continue;
                var mount = parts[5];
                if (mount == "/dev") continue;
                const double k = 1024.0;
                list.Add((mount, usedK * k, freeK * k, blocks * k));
            }
        }
        catch { }
        if (list.Count == 0) list.Add(("/", 0, 0, 0));
        _disks = list;
    }

    // ---- 网络（netstat -ib 累计字节差分）----

    private (long recv, long sent) GetNetTotals()
    {
        try
        {
            // 优先默认路由网卡（真正上网的那张），否则退化为“流量最大 en*”
            var routeIface = GetDefaultRouteIface();
            var best = -1L;
            var recv = 0L; var sent = 0L;
            var ns = Shell("netstat -ibn");
            foreach (var line in ns.Split('\n'))
            {
                var p = Regex.Split(line.Trim(), @"\s+");
                // 行：en0 1500 <Link#6> xx:xx... ipkts ierrs ibytes opkts oerrs obytes
                if (p.Length < 10 || !p[0].StartsWith("en", StringComparison.Ordinal)) continue;
                if (p.Length >= 4 && p[2].StartsWith("<Link", StringComparison.Ordinal) &&
                    long.TryParse(p[6], out var ib) && long.TryParse(p[9], out var ob))
                {
                    if (routeIface is not null && p[0] == routeIface)
                    {
                        recv = ib; sent = ob;
                        _defaultIface = p[0];
                        return (recv, sent);
                    }
                    if (ib + ob > best)
                    {
                        best = ib + ob;
                        recv = ib; sent = ob;
                        _defaultIface = p[0];
                    }
                }
            }
            return (recv, sent);
        }
        catch { return (0, 0); }
    }

    private static string? GetDefaultRouteIface()
    {
        try
        {
            var o = Shell("route -n get default 2>/dev/null");
            foreach (var line in o.Split('\n'))
            {
                if (!line.Contains("interface:", StringComparison.Ordinal)) continue;
                var c = line.IndexOf(':');
                if (c >= 0)
                {
                    var v = line[(c + 1)..].Trim();
                    if (v.Length > 0) return v;
                }
            }
        }
        catch { }
        return null;
    }

    private (double down, double up) GetNetSpeed(long recv, long sent, DateTime now)
    {
        if (!_netInited)
        {
            _netInited = true;
            _prevNetRecv = recv; _prevNetSent = sent; _prevNetTime = now;
            return (0, 0);
        }
        var seconds = (now - _prevNetTime).TotalSeconds;
        var down = seconds > 0 ? Math.Max(0, recv - _prevNetRecv) / seconds : 0;
        var up = seconds > 0 ? Math.Max(0, sent - _prevNetSent) / seconds : 0;
        _prevNetRecv = recv; _prevNetSent = sent; _prevNetTime = now;
        return (down, up);
    }

    private static string[] GetGateways()
    {
        var list = new List<string>();
        var o = Shell("route -n get default 2>/dev/null");
        foreach (var line in o.Split('\n'))
        {
            if (!line.Contains("gateway:", StringComparison.Ordinal)) continue;
            var c = line.IndexOf(':');
            if (c >= 0) { var v = line[(c + 1)..].Trim(); if (v.Length > 0) list.Add(v); }
        }
        return list.ToArray();
    }

    private static string[] GetDnsServers()
    {
        var set = new HashSet<string>();
        var o = Shell("scutil --dns 2>/dev/null");
        foreach (var line in o.Split('\n'))
        {
            if (!line.Contains("nameserver[", StringComparison.Ordinal)) continue;
            var c = line.LastIndexOf(':');
            if (c >= 0) { var v = line[(c + 1)..].Trim(); if (v.Length > 0) set.Add(v); }
        }
        return set.Take(3).ToArray();
    }

    private static string[] GetIps()
    {
        var ips = new List<string>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
            foreach (var ua in ni.GetIPProperties().UnicastAddresses)
            {
                if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    ips.Add(ua.Address.ToString());
            }
        }
        return ips.ToArray();
    }

    // ---- 进程 Top（TotalProcessorTime 差分，跨平台 .NET API）----

    private (IReadOnlyList<ProcessInfo> Items, int Count, int RunningCount) CollectProcesses(DateTime nowUtc, double totalMemBytes)
    {
        var items = new List<ProcessInfo>(256);
        var count = 0;
        var running = CountRunningByPs();
        var cores = Math.Max(1, Environment.ProcessorCount);
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                count++;
                try
                {
                    var pid = p.Id;
                    var cpuTime = p.TotalProcessorTime;
                    var ws = p.WorkingSet64;
                    var prev = _procPrev.TryGetValue(pid, out var v) ? v : default;
                    _procPrev[pid] = (cpuTime, nowUtc);

                    double cpuPercent = 0;
                    if (prev.Wall != default && nowUtc > prev.Wall && cpuTime >= prev.Cpu)
                    {
                        var deltaWall = (nowUtc - prev.Wall).TotalSeconds;
                        if (deltaWall > 0)
                            cpuPercent = (cpuTime - prev.Cpu).TotalSeconds / deltaWall / cores * 100.0;
                    }
                    var name = Safe(() => p.ProcessName) ?? "?";
                    var memPercent = totalMemBytes > 0 ? ws / totalMemBytes * 100.0 : 0;
                    items.Add(new ProcessInfo(name, pid, cpuPercent, memPercent, cpuTime.TotalSeconds));
                }
                catch { }
                finally { p.Dispose(); }
            }
        }
        catch { }
        // 修剪已退出进程缓存
        if (_procPrev.Count > items.Count + 64)
        {
            var seen = items.Select(x => x.Pid).ToHashSet();
            foreach (var pid in _procPrev.Keys.Where(pid => !seen.Contains(pid)).ToList()) _procPrev.Remove(pid);
        }
        return (items, count, running);
    }

    private IReadOnlyList<ProcessInfo> BuildTopActive(IReadOnlyList<ProcessInfo> items)
    {
        if (items.Count == 0) return Array.Empty<ProcessInfo>();
        return TopSort.ToLowerInvariant() switch
        {
            "mem" => items.OrderByDescending(x => x.MemPercent).Take(10).ToList(),
            "pid" => items.OrderBy(x => x.Pid).Take(10).ToList(),
            "name" => items.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase).Take(10).ToList(),
            _ => items.OrderByDescending(x => x.CpuPercent).Take(10).ToList(),
        };
    }

    /// <summary>macOS 无 .NET 线程状态枚举，用 ps 统计 R 状态进程数。</summary>
    private static int CountRunningByPs()
    {
        try
        {
            var o = Shell("ps -axo state=");
            var n = 0;
            foreach (var line in o.Split('\n'))
            {
                var s = line.Trim();
                if (s.Length > 0 && s[0] == 'R') n++;
            }
            return n;
        }
        catch { return 0; }
    }

    // ---- 系统信息 ----

    private string GetOsName()
    {
        if (_osName is not null) return _osName;
        try
        {
            var sw = Shell("sw_vers");
            var name = Regex.Match(sw, @"ProductName:\s*(.+)").Groups[1].Value.Trim();
            var ver = Regex.Match(sw, @"ProductVersion:\s*(.+)").Groups[1].Value.Trim();
            _osName = string.IsNullOrEmpty(name) ? "macOS" : name + " " + ver;
        }
        catch { _osName = "macOS"; }
        return _osName;
    }

    private string GetKernelVersion()
    {
        _kernelCache ??= Shell("uname -r").Trim();
        return _kernelCache;
    }

    private double GetCpuFrequencyMhz()
    {
        if (_freqCache is not null) return double.TryParse(_freqCache, out var v) ? v / 1e6 : 0;
        _freqCache = Shell("sysctl -n hw.cpufrequency 2>/dev/null").Trim();
        return double.TryParse(_freqCache, out var f) && f > 0 ? f / 1e6 : 0;
    }

    // ---- 电池（pmset）----

    private static (double percent, double seconds, string status) GetBattery()
    {
        try
        {
            var s = Shell("pmset -g batt");
            var m = Regex.Match(s, @"(\d+)%");
            var status = s.Contains("charging", StringComparison.OrdinalIgnoreCase) ? "charging"
                : s.Contains("discharging", StringComparison.OrdinalIgnoreCase) ? "discharging"
                : s.Contains("charged", StringComparison.OrdinalIgnoreCase) ? "full" : string.Empty;
            var sec = 0.0;
            var rm = Regex.Match(s, @"(\d+):(\d+)\s+remaining");
            if (rm.Success) sec = int.Parse(rm.Groups[1].Value) * 60 + int.Parse(rm.Groups[2].Value);
            return m.Success ? (double.Parse(m.Groups[1].Value), sec, status) : (-1, sec, status);
        }
        catch { return (-1, 0, string.Empty); }
    }

    private double AverageHistory(int seconds)
    {
        var count = Math.Min(seconds, _cpuHistory.Count);
        if (count == 0) return 0;
        var sum = 0.0;
        for (var i = _cpuHistory.Count - count; i < _cpuHistory.Count; i++) sum += _cpuHistory[i];
        return Math.Round(sum / count, 2);
    }

    private static string Shell(string args)
    {
        try
        {
            using var p = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "/bin/sh",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                }
            };
            p.StartInfo.ArgumentList.Add("-c");
            p.StartInfo.ArgumentList.Add(args);
            p.Start();
            var outp = p.StandardOutput.ReadToEnd();
            p.WaitForExit(2000);
            return outp;
        }
        catch { return string.Empty; }
    }

    private static T Safe<T>(Func<T> f)
    {
        try { return f(); } catch { return default!; }
    }

    // ---- mach P/Invoke ----
    private const int HOST_CPU_LOAD_INFO = 3;
    private const int PROCESSOR_CPU_LOAD_INFO = 2;

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int mach_host_self();

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int mach_task_self();

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int host_statistics(int host, int flavor, IntPtr info, ref int count);

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int host_processor_info(int host, int flavor, ref uint processorCount,
        out IntPtr processorInfo, ref int processorInfoCount);

    [DllImport("/usr/lib/libSystem.B.dylib")]
    private static extern int vm_deallocate(int target, IntPtr address, int size);

    public void Dispose() { }
}
