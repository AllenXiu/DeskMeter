using System.Diagnostics;
using System.Net.NetworkInformation;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace DeskMeter.Core.Data;

/// <summary>
/// 系统数据采集器（≈ Conky data/ + data/os/）：每次刷新同步采集 CPU/内存/磁盘/网络/进程等。
/// 所有 Windows API 调用均容错——失败时返回 0/空，保证不崩溃（FR-VAR-2）。
/// </summary>
public sealed class SystemDataCollector : IDisposable
{
    // ---- 状态 ----
    private long _prevIdle, _prevKernel, _prevUser;
    private bool _cpuInited;
    private long _prevNetRecv, _prevNetSent;
    private DateTime _prevNetTime;
    private bool _netInited;
    private NetworkInterface? _netInterface; // 缓存复用：每 tick 不再重新枚举网络接口（原生缓冲不再累积）
    private int _netRescanCounter;
    private IReadOnlyList<string>? _driveRoots; // 磁盘根缓存：60 tick 重扫一次
    private int _driveRescanCounter;
    private IReadOnlyList<string> _ifaceIps = Array.Empty<string>();
    private IReadOnlyList<string> _gatewayIps = Array.Empty<string>();
    private IReadOnlyList<string> _dnsServers = Array.Empty<string>();
    private string _defaultIface = string.Empty;
    private int _netInfoRescan;
    private readonly List<double> _cpuHistory = new(); // loadavg：最近 900 秒 CPU 占用历史
    private PerformanceCounter? _diskReadCounter;
    private PerformanceCounter? _diskWriteCounter;

    // ---- 任务管理器式 Top 扩展指标（磁盘 IO / GPU / 连接数，按需缓存采样）----
    public string TopSort { get; set; } = "cpu"; // cpu|mem|pid|name|disk|gpu|net

    // 内存优化：仅当配置用到对应列/排序键时才采集（默认关，由宿主按配置开启）
    public bool CollectDiskMetrics { get; set; }
    public bool CollectGpuMetrics { get; set; }
    public bool CollectNetMetrics { get; set; }
    private Dictionary<int, string>? _procDiskInstances;
    private int _diskInstanceRescan;
    private readonly Dictionary<string, PerformanceCounter> _ioReadCounters = new();
    private readonly Dictionary<string, PerformanceCounter> _ioWriteCounters = new();
    private Dictionary<int, List<string>>? _gpuInstances;
    private int _gpuInstanceRescan;
    private readonly Dictionary<string, PerformanceCounter> _gpuCounters = new();
    private Dictionary<int, int>? _conns;
    private int _connTick;
    private const int ConnSampleEvery = 3;
    private string? _freqCache;
    private string? _osNameCache;
    private string? _kernelCache;
    private bool _osInited;
    private readonly Dictionary<int, PerformanceCounter> _coreCounters = new();
    private TemperatureMonitor? _temperature;

    /// <summary>是否启用温度采集（默认启用；deskmeter.temperature=false 可关）。</summary>
    public TemperatureMonitor? Temperature => _temperature;

    public SystemDataCollector(bool enableTemperature = true)
    {
        _temperature = enableTemperature ? new TemperatureMonitor(true) : null;
    }

    public SystemDataCollector() : this(true) { }

    public SystemSnapshot Collect()
    {
        var now = DateTime.Now;
        var cpu = GetCpuPercent();
        _cpuHistory.Add(cpu);
        if (_cpuHistory.Count > 900) _cpuHistory.RemoveAt(0);
        EnsureNetworkInfo();
        var (recv, sent) = GetNetTotals();
        var speed = GetNetSpeed(recv, sent, now);
        var (memUsed, memTotal, swapUsed, swapTotal) = GetMemory();
        // 进程数据每 tick 只枚举一次（计数 + 运行数 + top 榜共用），并全部 Dispose 防止句柄/内存泄漏
        var procSnap = CollectProcesses(DateTime.UtcNow, memTotal);
        var (topCpu, topMem) = GetTopProcesses(procSnap.Items, 10);
        var topActive = BuildTopActive(procSnap.Items);
        var (diskRead, diskWrite) = GetDiskIo();
        var (battPercent, battSeconds, battStatus) = GetBattery();

        var snap = new SystemSnapshot
        {
            CpuPercent = cpu,
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
            Machine = Environment.Is64BitOperatingSystem ? "x86_64" : "x86",
            TopCpu = topCpu,
            TopMem = topMem,
            TopActive = topActive,
            CpuCoresPercent = GetCpuCores(),
            InterfaceIps = _ifaceIps,
            GatewayIps = _gatewayIps,
            DnsServers = _dnsServers,
            DefaultInterfaceName = _defaultIface,
            LoadAvg1 = AverageHistory(60),
            LoadAvg5 = AverageHistory(300),
            LoadAvg15 = AverageHistory(900),
            DiskReadBytesPerSec = diskRead,
            DiskWriteBytesPerSec = diskWrite,
            BatteryPercent = battPercent,
            BatteryRemainingSeconds = battSeconds,
            BatteryStatus = battStatus,
        };

        if (_temperature is not null)
        {
            var (cpuT, gpuT, diskT) = _temperature.Snapshot();
            snap.SetTemperatures(cpuT, gpuT, diskT);
        }

        // 磁盘：常见根路径
        foreach (var root in GetDriveRoots())
        {
            var info = GetDisk(root);
            if (info is not null) snap.SetDisk(root, info);
        }

        return snap;
    }

    public void Dispose()
    {
        _temperature?.Dispose();
        _temperature = null;
        foreach (var pc in _coreCounters.Values)
        {
            try { pc.Dispose(); } catch { }
        }
        _coreCounters.Clear();
        try { _diskReadCounter?.Dispose(); } catch { }
        try { _diskWriteCounter?.Dispose(); } catch { }
        foreach (var pc in _ioReadCounters.Values.Concat(_ioWriteCounters.Values).Concat(_gpuCounters.Values))
        {
            try { pc.Dispose(); } catch { }
        }
    }

    // ---- CPU ----

    private double GetCpuPercent()
    {
        if (!GetSystemTimes(out var idle, out var kernel, out var user)) return 0;
        var idleT = ToTicks(idle);
        var kernelT = ToTicks(kernel);
        var userT = ToTicks(user);
        if (!_cpuInited)
        {
            _cpuInited = true;
            _prevIdle = idleT; _prevKernel = kernelT; _prevUser = userT;
            return 0; // 首个采样点无增量
        }
        var idleDelta = idleT - _prevIdle;
        var totalDelta = (kernelT + userT) - (_prevKernel + _prevUser);
        _prevIdle = idleT; _prevKernel = kernelT; _prevUser = userT;
        if (totalDelta <= 0) return 0;
        var busy = totalDelta - idleDelta;
        return Math.Clamp(busy * 100.0 / totalDelta, 0, 100);
    }

    private static long ToTicks(System.Runtime.InteropServices.ComTypes.FILETIME ft) =>
        ((long)ft.dwHighDateTime << 32) | (uint)ft.dwLowDateTime;

    // ---- 每核 CPU（PerformanceCounter，失败回退空列表）----

    private double[] GetCpuCores()
    {
        try
        {
            var cores = Environment.ProcessorCount;
            var result = new double[cores];
            for (var i = 0; i < cores; i++)
            {
                try
                {
                    if (!_coreCounters.TryGetValue(i, out var pc))
                    {
                        pc = new PerformanceCounter("Processor", "% Processor Time", i.ToString());
                        _coreCounters[i] = pc;
                        pc.NextValue(); // 首采样基线
                        result[i] = 0;
                    }
                    else
                    {
                        result[i] = Math.Clamp(pc.NextValue(), 0, 100);
                    }
                }
                catch
                {
                    result[i] = 0;
                }
            }
            return result;
        }
        catch
        {
            return Array.Empty<double>();
        }
    }

    // ---- 内存（GlobalMemoryStatusEx）----

    private static (double used, double total, double swapUsed, double swapTotal) GetMemory()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)Marshal.SizeOf<MEMORYSTATUSEX>() };
        if (!GlobalMemoryStatusEx(ref m)) return (0, 0, 0, 0);
        var total = (double)m.ullTotalPhys;
        var used = total - m.ullAvailPhys;
        var swapTotal = (double)m.ullTotalPageFile;
        var swapUsed = swapTotal - m.ullAvailPageFile;
        return (used, total, swapUsed, swapTotal);
    }

    // ---- 磁盘 ----

    private IReadOnlyList<string> GetDriveRoots()
    {
        if (_driveRoots is null || ++_driveRescanCounter >= 60)
        {
            _driveRescanCounter = 0;
            try
            {
                _driveRoots = DriveInfo.GetDrives()
                    .Where(d => d.IsReady)
                    .Select(d => d.RootDirectory.FullName)
                    .ToList();
            }
            catch
            {
                _driveRoots = new[] { SystemSnapshot.NormalizeDiskPath("/") };
            }
        }
        return _driveRoots;
    }

    private static DiskInfo? GetDisk(string root)
    {
        var path = root is "/" or "\\" ? Path.GetPathRoot(Environment.SystemDirectory) ?? root : root;
        if (!GetDiskFreeSpaceEx(path, out var freeAvail, out var total, out _)) return null;
        return new DiskInfo { Total = total, Free = freeAvail, Used = total - freeAvail };
    }

    // ---- 网络信息（IP/网关/DNS/默认网卡；60 tick 缓存）----

    private void EnsureNetworkInfo()
    {
        if (++_netInfoRescan < 60 && _ifaceIps.Count > 0) return;
        _netInfoRescan = 0;
        var ips = new List<string>();
        var gateways = new List<string>();
        var dns = new List<string>();
        string defaultIface = string.Empty;
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                var props = ni.GetIPProperties();
                foreach (var ua in props.UnicastAddresses)
                {
                    if (ua.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                    {
                        ips.Add(ua.Address.ToString());
                        if (ni.OperationalStatus == OperationalStatus.Up && defaultIface.Length == 0)
                            defaultIface = ni.Name;
                    }
                }
                foreach (var ga in props.GatewayAddresses)
                {
                    if (ga.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        gateways.Add(ga.Address.ToString());
                }
                foreach (var da in props.DnsAddresses)
                {
                    if (da.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)
                        dns.Add(da.ToString());
                }
            }
        }
        catch { /* 容错 */ }
        _ifaceIps = ips;
        _gatewayIps = gateways;
        _dnsServers = dns;
        _defaultIface = defaultIface;
    }

    private double AverageHistory(int seconds)
    {
        var count = Math.Min(seconds, _cpuHistory.Count);
        if (count == 0) return 0;
        var sum = 0.0;
        for (var i = _cpuHistory.Count - count; i < _cpuHistory.Count; i++) sum += _cpuHistory[i];
        return Math.Round(sum / count, 2);
    }

    // ---- 网络 ----

    private (long recv, long sent) GetNetTotals()
    {
        try
        {
            // 每 60 tick 重扫一次接口（罕见变化）；平时复用对象只取新统计，避免每 tick 枚举产生的原生缓冲累积
            if (_netInterface is null || ++_netRescanCounter >= 60)
            {
                _netRescanCounter = 0;
                NetworkInterface? best = null;
                long bestBytes = -1;
                foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
                {
                    if (ni.OperationalStatus != OperationalStatus.Up) continue;
                    if (ni.NetworkInterfaceType == NetworkInterfaceType.Loopback) continue;
                    var stats = ni.GetIPv4Statistics();
                    var bytes = stats.BytesReceived + stats.BytesSent;
                    if (bytes > bestBytes) { bestBytes = bytes; best = ni; }
                }
                _netInterface = best;
            }
            if (_netInterface is not null)
            {
                var s = _netInterface.GetIPv4Statistics();
                return (s.BytesReceived, s.BytesSent);
            }
            return (0, 0);
        }
        catch
        {
            return (0, 0);
        }
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

    // ---- 进程（Conky update_top 等价物：进程 CPU/MEM 采样排序；每 tick 只枚举一次并全部 Dispose）----

    private readonly Dictionary<int, (TimeSpan Cpu, DateTime Wall)> _procPrev = new();

    private readonly record struct ProcessSnapshot(IReadOnlyList<ProcessInfo> Items, int Count, int RunningCount);

    private ProcessSnapshot CollectProcesses(DateTime nowUtc, double totalMemBytes)
    {
        var items = new List<ProcessInfo>(256);
        var seen = new HashSet<int>(256);
        var count = 0;
        var running = 0;
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
                    seen.Add(pid);

                    double cpuPercent = 0;
                    if (prev.Wall != default && nowUtc > prev.Wall && cpuTime >= prev.Cpu)
                    {
                        var deltaWall = (nowUtc - prev.Wall).TotalSeconds;
                        if (deltaWall > 0)
                        {
                            cpuPercent = (cpuTime - prev.Cpu).TotalSeconds / deltaWall / cores * 100.0;
                        }
                    }
                    var name = Safe(() => p.ProcessName) ?? "?";
                    var memPercent = totalMemBytes > 0 ? ws / totalMemBytes * 100.0 : 0;
                    items.Add(new ProcessInfo(name, pid, cpuPercent, memPercent, cpuTime.TotalSeconds));

                    // 运行中进程数（含任一 Running 线程）；ProcessThread 同样需要 Dispose
                    try
                    {
                        foreach (ProcessThread t in p.Threads)
                        {
                            try
                            {
                                if (t.ThreadState == System.Diagnostics.ThreadState.Running) { running++; break; }
                            }
                            finally { t.Dispose(); }
                        }
                    }
                    catch { /* 访问受限进程跳过 */ }
                }
                catch
                {
                    // 访问受限进程跳过
                }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch
        {
            // 整体失败返回空榜
        }

        // 修剪已退出进程的采样缓存（保持字典有界，不再整表清空）
        if (_procPrev.Count > seen.Count + 64)
        {
            foreach (var pid in _procPrev.Keys.Where(pid => !seen.Contains(pid)).ToList())
                _procPrev.Remove(pid);
        }

        return new ProcessSnapshot(items, count, running);
    }

    private static (IReadOnlyList<ProcessInfo> cpu, IReadOnlyList<ProcessInfo> mem) GetTopProcesses(
        IReadOnlyList<ProcessInfo> items, int count)
    {
        if (items.Count == 0) return (Array.Empty<ProcessInfo>(), Array.Empty<ProcessInfo>());
        return (
            items.OrderByDescending(x => x.CpuPercent).Take(count).ToList(),
            items.OrderByDescending(x => x.MemPercent).Take(count).ToList());
    }
    /// <summary>按 deskmeter.top.sort 构建当前 Top 榜（${top ...} 用）；扩展指标只对候选进程采样控制开销。</summary>
    private IReadOnlyList<ProcessInfo> BuildTopActive(IReadOnlyList<ProcessInfo> items)
    {
        if (items.Count == 0) return Array.Empty<ProcessInfo>();

        // 候选 = CPU 前 20 ∪ 内存前 20（磁盘/GPU/连接数只对这些进程采集）
        var candidates = items.OrderByDescending(x => x.CpuPercent).Take(20)
            .Concat(items.OrderByDescending(x => x.MemPercent).Take(20))
            .Select(x => x.Pid).Distinct().ToHashSet();
        if (CollectDiskMetrics) EnsureProcDiskInstances();
        if (CollectGpuMetrics) EnsureGpuInstances();
        if (CollectNetMetrics) EnsureConnections();
        foreach (var item in items)
        {
            if (!candidates.Contains(item.Pid)) continue;
            if (CollectDiskMetrics)
            {
                item.DiskReadBytesPerSec = DiskIoFor(item.Pid, read: true);
                item.DiskWriteBytesPerSec = DiskIoFor(item.Pid, read: false);
            }
            if (CollectGpuMetrics) item.GpuPercent = GpuFor(item.Pid);
            if (CollectNetMetrics) item.NetConnections = _conns is not null && _conns.TryGetValue(item.Pid, out var c) ? c : 0;
        }

        IOrderedEnumerable<ProcessInfo> ordered = TopSort.ToLowerInvariant() switch
        {
            "mem" => items.OrderByDescending(x => x.MemPercent),
            "pid" => items.OrderBy(x => x.Pid),
            "name" => items.OrderBy(x => x.Name, StringComparer.OrdinalIgnoreCase),
            "disk" => items.OrderByDescending(x => x.DiskReadBytesPerSec + x.DiskWriteBytesPerSec),
            "gpu" => items.OrderByDescending(x => x.GpuPercent),
            "net" => items.OrderByDescending(x => x.NetConnections),
            _ => items.OrderByDescending(x => x.CpuPercent),
        };
        return ordered.Take(10).ToList();
    }

    // ---- 每进程磁盘 IO（PerfMon Process\IO Read/Write Bytes/sec）----

    private void EnsureProcDiskInstances()
    {
        if (_procDiskInstances is not null && ++_diskInstanceRescan < 60) return;
        _diskInstanceRescan = 0;
        var map = new Dictionary<int, string>();
        try
        {
            foreach (var name in new PerformanceCounterCategory("Process").GetInstanceNames())
            {
                var hash = name.LastIndexOf('#');
                if (hash <= 0 || !int.TryParse(name[(hash + 1)..], out var pid)) continue;
                map[pid] = name;
            }
        }
        catch { }
        _procDiskInstances = map;
    }

    private double DiskIoFor(int pid, bool read)
    {
        try
        {
            if (_procDiskInstances is null || !_procDiskInstances.TryGetValue(pid, out var inst)) return 0;
            var cache = read ? _ioReadCounters : _ioWriteCounters;
            if (!cache.TryGetValue(inst, out var pc))
            {
                pc = new PerformanceCounter("Process", read ? "IO Read Bytes/sec" : "IO Write Bytes/sec", inst);
                cache[inst] = pc;
                pc.NextValue(); // 首采样基线
            }
            return Math.Max(0, pc.NextValue());
        }
        catch
        {
            return 0;
        }
    }

    // ---- 每进程 GPU（PerfMon GPU Engine\Utilization Percentage，多引擎取最大）----

    private void EnsureGpuInstances()
    {
        if (_gpuInstances is not null && ++_gpuInstanceRescan < 60) return;
        _gpuInstanceRescan = 0;
        var map = new Dictionary<int, List<string>>();
        try
        {
            foreach (var name in new PerformanceCounterCategory("GPU Engine").GetInstanceNames())
            {
                if (!name.StartsWith("pid_", StringComparison.Ordinal)) continue;
                var underscore = name.IndexOf('_', 4);
                if (underscore <= 0 || !int.TryParse(name.AsSpan(4, underscore - 4), out var pid)) continue;
                if (!map.TryGetValue(pid, out var list)) map[pid] = list = new List<string>();
                list.Add(name);
            }
        }
        catch { }
        _gpuInstances = map;
    }

    private double GpuFor(int pid)
    {
        try
        {
            if (_gpuInstances is null || !_gpuInstances.TryGetValue(pid, out var list) || list.Count == 0) return 0;
            double max = 0;
            foreach (var inst in list)
            {
                if (!_gpuCounters.TryGetValue(inst, out var pc))
                {
                    pc = new PerformanceCounter("GPU Engine", "Utilization Percentage", inst);
                    _gpuCounters[inst] = pc;
                    pc.NextValue();
                }
                max = Math.Max(max, Math.Max(0, pc.NextValue()));
            }
            return max;
        }
        catch
        {
            return 0;
        }
    }

    // ---- 每进程网络连接数（GetExtendedTcpTable/UdpTable，3 tick 采一次）----

    private void EnsureConnections()
    {
        if (++_connTick % ConnSampleEvery != 0 && _conns is not null) return;
        var map = new Dictionary<int, int>();
        try
        {
            foreach (var pid in GetTcpOwnerPids()) map[pid] = map.TryGetValue(pid, out var c) ? c + 1 : 1;
            foreach (var pid in GetUdpOwnerPids()) map[pid] = map.TryGetValue(pid, out var c) ? c + 1 : 1;
        }
        catch { }
        _conns = map;
    }

    private static IEnumerable<int> GetTcpOwnerPids()
    {
        var size = 0;
        if (GetExtendedTcpTable(IntPtr.Zero, ref size, false, 2, 5, 0) != 0 || size <= 0) yield break;
        var buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedTcpTable(buf, ref size, false, 2, 5, 0) != 0) yield break;
            var count = Marshal.ReadInt32(buf);
            for (var i = 0; i < count; i++)
            {
                var row = IntPtr.Add(buf, 4 + i * 24);
                yield return Marshal.ReadInt32(row, 20); // dwOwningPid
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    private static IEnumerable<int> GetUdpOwnerPids()
    {
        var size = 0;
        if (GetExtendedUdpTable(IntPtr.Zero, ref size, false, 2, 1, 0) != 0 || size <= 0) yield break;
        var buf = Marshal.AllocHGlobal(size);
        try
        {
            if (GetExtendedUdpTable(buf, ref size, false, 2, 1, 0) != 0) yield break;
            var count = Marshal.ReadInt32(buf);
            for (var i = 0; i < count; i++)
            {
                var row = IntPtr.Add(buf, 4 + i * 12);
                yield return Marshal.ReadInt32(row, 8); // dwOwningPid
            }
        }
        finally { Marshal.FreeHGlobal(buf); }
    }

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedTcpTable(IntPtr pTcpTable, ref int dwOutBufLen, bool sort,
        uint ipVersion, uint tblClass, uint reserved);

    [DllImport("iphlpapi.dll", SetLastError = true)]
    private static extern uint GetExtendedUdpTable(IntPtr pUdpTable, ref int dwOutBufLen, bool sort,
        uint ipVersion, uint tblClass, uint reserved);

    // ---- 静态信息（带缓存，容错）----

    private double GetCpuFrequencyMhz()
    {
        if (_freqCache is not null) return _freqCache == "-" ? 0 : double.Parse(_freqCache, System.Globalization.CultureInfo.InvariantCulture);
        var value = Safe(() =>
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\CentralProcessor\0");
            return key?.GetValue("~MHz")?.ToString();
        });
        _freqCache = value ?? "-";
        return _freqCache == "-" ? 0 : double.Parse(_freqCache, System.Globalization.CultureInfo.InvariantCulture);
    }

    private string GetOsName()
    {
        if (_osInited) return _osNameCache ?? "Windows";
        _osInited = true;
        var product = Safe(() =>
        {
            using var key = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows NT\CurrentVersion");
            return key?.GetValue("ProductName")?.ToString();
        });
        _osNameCache = string.IsNullOrEmpty(product) ? "Windows" : product!;
        return _osNameCache;
    }

    private string GetKernelVersion()
    {
        if (_kernelCache is not null) return _kernelCache;
        try { _kernelCache = Environment.OSVersion.Version.ToString(); }
        catch { _kernelCache = "10.0"; }
        return _kernelCache;
    }

    private static T? Safe<T>(Func<T> f)
    {
        try { return f(); }
        catch { return default; }
    }

    // ---- 磁盘 IO 速率（PhysicalDisk 计数器，字节/秒）----

    private (double read, double write) GetDiskIo()
    {
        try
        {
            _diskReadCounter ??= new PerformanceCounter("PhysicalDisk", "Disk Read Bytes/sec", "_Total");
            _diskWriteCounter ??= new PerformanceCounter("PhysicalDisk", "Disk Write Bytes/sec", "_Total");
            return (Math.Max(0, _diskReadCounter.NextValue()), Math.Max(0, _diskWriteCounter.NextValue()));
        }
        catch
        {
            return (0, 0);
        }
    }

    // ---- 电池（GetSystemPowerStatus，无依赖）----

    private static (double percent, double seconds, string status) GetBattery()
    {
        try
        {
            if (!GetSystemPowerStatus(out var sps)) return (-1, 0, string.Empty);
            var percent = sps.BatteryLifePercent == 255 ? -1 : sps.BatteryLifePercent;
            var seconds = sps.BatteryLifeTime == uint.MaxValue ? 0 : sps.BatteryLifeTime;
            string status;
            if (sps.ACLineStatus == 1)
                status = (sps.BatteryFlag & 8) != 0 ? "charging" : "full";
            else
                status = "discharging";
            return (percent, seconds, status);
        }
        catch
        {
            return (-1, 0, string.Empty);
        }
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct SYSTEM_POWER_STATUS
    {
        public byte ACLineStatus;
        public byte BatteryFlag;
        public byte BatteryLifePercent;
        public byte SystemStatusFlag;
        public uint BatteryLifeTime;
        public uint BatteryFullLifeTime;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemPowerStatus(out SYSTEM_POWER_STATUS lpSystemPowerStatus);

    // ---- P/Invoke ----

    [StructLayout(LayoutKind.Sequential)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength;
        public uint dwMemoryLoad;
        public ulong ullTotalPhys;
        public ulong ullAvailPhys;
        public ulong ullTotalPageFile;
        public ulong ullAvailPageFile;
        public ulong ullTotalVirtual;
        public ulong ullAvailVirtual;
        public ulong ullAvailExtendedVirtual;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetDiskFreeSpaceEx(string lpDirectoryName,
        out ulong lpFreeBytesAvailableToCaller, out ulong lpTotalNumberOfBytes,
        out ulong lpTotalNumberOfFreeBytes);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool GetSystemTimes(
        out System.Runtime.InteropServices.ComTypes.FILETIME lpIdleTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME lpKernelTime,
        out System.Runtime.InteropServices.ComTypes.FILETIME lpUserTime);
}
