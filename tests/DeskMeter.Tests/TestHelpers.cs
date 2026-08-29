using DeskMeter.Core.Config;
using DeskMeter.Core.Data;

namespace DeskMeter.Tests;

internal static class TestHelpers
{
    /// <summary>定位仓库根目录（含 DeskMeter.sln 的目录）。</summary>
    public static string FindRepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null && !File.Exists(Path.Combine(dir.FullName, "DeskMeter.sln")))
            dir = dir.Parent;
        return dir?.FullName ?? throw new InvalidOperationException("找不到仓库根目录（DeskMeter.sln）");
    }

    /// <summary>固定数据的快照，供渲染断言使用。</summary>
    public static SystemSnapshot FakeSnapshot()
    {
        var snap = new SystemSnapshot
        {
        CpuPercent = 12,
        MemUsedBytes = 4.2 * 1024 * 1024 * 1024,
        MemTotalBytes = 16L * 1024L * 1024L * 1024L,
        SwapUsedBytes = 1024 * 1024 * 1024,
        SwapTotalBytes = 8L * 1024L * 1024L * 1024L,
        CpuFrequencyMhz = 3600,
        ProcessCount = 245,
        RunningProcessCount = 3,
        UpSpeedBytesPerSec = 120 * 1024,
        DownSpeedBytesPerSec = 2.4 * 1024 * 1024,
        TotalUpBytes = (long)(3.1 * 1024 * 1024 * 1024),
        TotalDownBytes = (long)(12.5 * 1024 * 1024 * 1024),
        Now = new DateTime(2025, 1, 1, 14, 35, 30),
        Uptime = TimeSpan.FromDays(3) + TimeSpan.FromHours(4) + TimeSpan.FromMinutes(12),
        TopCpu = new[]
        {
            new ProcessInfo("firefox", 4821, 12.4, 8.2),
            new ProcessInfo("chrome", 100, 5.0, 3.0),
        },
        TopMem = new[]
        {
            new ProcessInfo("chrome", 100, 5.0, 20.1),
            new ProcessInfo("firefox", 4821, 12.4, 8.2),
        },
        HostName = "DESKTOP-ABC123",
        OsName = "Microsoft Windows 11 Pro",
        KernelVersion = "10.0.22631",
        Machine = "x86_64",
        CpuCoresPercent = new[] { 10.0, 20.0, 30.0, 40.0 },
    };
        snap.SetTemperatures(new[] { 40.0, 42.0, 41.0 }, new[] { 55.0 }, new[] { 30.0 });
        return snap;
    }

    public static ConfigSettings Settings(Dictionary<string, object?>? extra = null)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase)
        {
            ["update_interval"] = 2.0,
            ["alignment"] = "top_right",
            ["gap_x"] = 16.0,
            ["gap_y"] = 16.0,
            ["font"] = "Consolas:size=12",
            ["default_color"] = "FFFFFF",
            ["color0"] = "88CCFF",
        };
        if (extra is not null)
            foreach (var (k, v) in extra) values[k] = v;
        return new ConfigSettings("", values);
    }
}
