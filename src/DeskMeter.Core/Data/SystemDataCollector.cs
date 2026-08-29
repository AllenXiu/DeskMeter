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
    private string? _freqCache;
    private string? _osNameCache;
    private string? _kernelCache;
    private bool _osInited;

    public SystemSnapshot Collect()
    {
        var now = DateTime.Now;
        var cpu = GetCpuPercent();
        var (recv, sent) = GetNetTotals();
        var speed = GetNetSpeed(recv, sent, now);
        var (memUsed, memTotal, swapUsed, swapTotal) = GetMemory();

        var snap = new SystemSnapshot
        {
            CpuPercent = cpu,
            MemUsedBytes = memUsed,
            MemTotalBytes = memTotal,
            SwapUsedBytes = swapUsed,
            SwapTotalBytes = swapTotal,
            CpuFrequencyMhz = GetCpuFrequencyMhz(),
            ProcessCount = GetProcessCount(),
            RunningProcessCount = GetRunningProcessCount(),
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
        };

        // 磁盘：常见根路径
        foreach (var root in GetDriveRoots())
        {
            var info = GetDisk(root);
            if (info is not null) snap.SetDisk(root, info);
        }

        return snap;
    }

    public void Dispose() { }

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

    private static IEnumerable<string> GetDriveRoots()
    {
        try
        {
            return DriveInfo.GetDrives()
                .Where(d => d.IsReady)
                .Select(d => d.RootDirectory.FullName)
                .ToList();
        }
        catch
        {
            return new[] { SystemSnapshot.NormalizeDiskPath("/") };
        }
    }

    private static DiskInfo? GetDisk(string root)
    {
        var path = root is "/" or "\\" ? Path.GetPathRoot(Environment.SystemDirectory) ?? root : root;
        if (!GetDiskFreeSpaceEx(path, out var freeAvail, out var total, out _)) return null;
        return new DiskInfo { Total = total, Free = freeAvail, Used = total - freeAvail };
    }

    // ---- 网络 ----

    private static (long recv, long sent) GetNetTotals()
    {
        try
        {
            long recv = 0, sent = 0;
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
            if (best is not null)
            {
                var s = best.GetIPv4Statistics();
                recv = s.BytesReceived;
                sent = s.BytesSent;
            }
            return (recv, sent);
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

    // ---- 进程 ----

    private static int GetProcessCount()
    {
        try { return Process.GetProcesses().Length; }
        catch { return 0; }
    }

    private static int GetRunningProcessCount()
    {
        var count = 0;
        try
        {
            foreach (var p in Process.GetProcesses())
            {
                try
                {
                    if (p.Threads.Cast<ProcessThread>().Any(t => t.ThreadState == System.Diagnostics.ThreadState.Running)) count++;
                }
                catch { /* 访问受限进程跳过 */ }
            }
        }
        catch { /* 整体失败返回 0 */ }
        return count;
    }

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
