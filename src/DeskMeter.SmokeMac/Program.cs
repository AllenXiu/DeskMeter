using DeskMeter.Core.Data;
using DeskMeter.Core.Text;

using var c = new MacSystemDataCollector();
for (var round = 0; round < 3; round++)
{
    var s = c.Collect();
    var fmt = (double b) => HumanBytes.Format(b);
    Console.WriteLine($"--- round {round} ---");
    Console.WriteLine($"cpu:      {s.CpuPercent:F1}%   cores: {string.Join(",", s.CpuCoresPercent.Select(x => x.ToString("F0")))}");
    Console.WriteLine($"mem:      {fmt(s.MemUsedBytes)} / {fmt(s.MemTotalBytes)} ({s.MemPercent:F1}%)");
    Console.WriteLine($"swap:     {fmt(s.SwapUsedBytes)} / {fmt(s.SwapTotalBytes)} ({s.SwapPercent:F1}%)");
    var root = s.GetDisk("/");
    Console.WriteLine($"disk /:   used={fmt(root.Used)} free={fmt(root.Free)} total={fmt(root.Total)} ({root.FreePercent:F1}% free)");
    Console.WriteLine($"net:      down={fmt(s.DownSpeedBytesPerSec)}/s up={fmt(s.UpSpeedBytesPerSec)}/s total(d)={fmt(s.TotalDownBytes)}");
    Console.WriteLine($"os:       {s.OsName} | kernel={s.KernelVersion} | {s.Machine} | host={s.HostName}");
    Console.WriteLine($"procs:    {s.ProcessCount} | up={s.Uptime:c} | iface={s.DefaultInterfaceName} ips={string.Join(",", s.InterfaceIps)}");
    Console.WriteLine($"top cpu:  " + string.Join(" | ", s.TopCpu.Take(3).Select(p => $"{p.Name} {p.CpuPercent:F1}%")));
    Console.WriteLine($"top mem:  " + string.Join(" | ", s.TopMem.Take(3).Select(p => $"{p.Name} {p.MemPercent:F1}%")));
    if (round == 0) await Task.Delay(1200);
}
