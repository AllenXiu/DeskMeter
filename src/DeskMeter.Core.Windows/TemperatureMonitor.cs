using LibreHardwareMonitor.Hardware;

namespace DeskMeter.Core.Data;

/// <summary>
/// 温度采集器（P2）：LibreHardwareMonitor 后台线程每 2s 更新 CPU/GPU/磁盘温度传感器。
/// 无权限/无传感器时列表为空，变量层显示占位（FR-VAR-2），绝不报错。
/// </summary>
public sealed class TemperatureMonitor : IDisposable
{
    private readonly object _lock = new();
    private readonly CancellationTokenSource _cts = new();
    private readonly Task? _loop;
    private Computer? _computer;
    private List<double> _cpuTemps = new();
    private List<double> _gpuTemps = new();
    private List<double> _diskTemps = new();

    public TemperatureMonitor(bool enabled)
    {
        if (!enabled) return;
        try
        {
            _computer = new Computer
            {
                IsCpuEnabled = true,
                IsGpuEnabled = true,
                IsStorageEnabled = true,
                IsMotherboardEnabled = true,
            };
            _computer.Open();
            _loop = Task.Run(LoopAsync);
        }
        catch
        {
            // 环境不支持（如沙箱/无管理员权限）：保持空列表
            try { _computer?.Close(); } catch { }
            _computer = null;
        }
    }

    private async Task LoopAsync()
    {
        try
        {
            while (!_cts.IsCancellationRequested)
            {
                Update();
                await Task.Delay(2000, _cts.Token).ConfigureAwait(false);
            }
        }
        catch (OperationCanceledException) { }
        catch { /* 采集异常忽略 */ }
    }

    private void Update()
    {
        try
        {
            var computer = _computer;
            if (computer is null) return;
            var cpu = new List<double>();
            var gpu = new List<double>();
            var disk = new List<double>();
            computer.Accept(new SensorVisitor(sensor =>
            {
                if (sensor.SensorType != SensorType.Temperature || sensor.Value is not { } v) return;
                switch (sensor.Hardware?.HardwareType)
                {
                    case HardwareType.Cpu: cpu.Add(v); break;
                    case HardwareType.GpuAmd:
                    case HardwareType.GpuNvidia: gpu.Add(v); break;
                    case HardwareType.Storage: disk.Add(v); break;
                }
            }));

            lock (_lock)
            {
                _cpuTemps = cpu;
                _gpuTemps = gpu;
                _diskTemps = disk;
            }
        }
        catch { }
    }

    public (IReadOnlyList<double> Cpu, IReadOnlyList<double> Gpu, IReadOnlyList<double> Disk) Snapshot()
    {
        lock (_lock)
        {
            return (_cpuTemps.ToList(), _gpuTemps.ToList(), _diskTemps.ToList());
        }
    }

    public void Dispose()
    {
        _cts.Cancel();
        try { _loop?.Wait(1000); } catch { }
        try { _computer?.Close(); } catch { }
        _computer = null;
        _cts.Dispose();
    }
}
