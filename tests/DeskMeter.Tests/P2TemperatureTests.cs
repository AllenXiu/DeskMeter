using DeskMeter.Core.Config;
using DeskMeter.Core.Objects;
using Xunit;

namespace DeskMeter.Tests;

public class P2TemperatureTests
{
    private readonly ConfigSettings _settings = TestHelpers.Settings();

    [Fact]
    public void Platform_MapCoretempToCpuTemps()
    {
        Assert.Equal("40", VariableEvaluator.Evaluate("platform", new[] { "coretemp.0", "temp", "1" }, TestHelpers.FakeSnapshot(), _settings));
        Assert.Equal("41", VariableEvaluator.Evaluate("platform", new[] { "coretemp.0", "temp", "3" }, TestHelpers.FakeSnapshot(), _settings));
    }

    [Fact]
    public void Platform_OutOfRange_ReturnsNullPlaceholder()
    {
        Assert.Null(VariableEvaluator.Evaluate("platform", new[] { "coretemp.0", "temp", "9" }, TestHelpers.FakeSnapshot(), _settings));
    }

    [Fact]
    public void Platform_MapsGpuAndDisk()
    {
        Assert.Equal("55", VariableEvaluator.Evaluate("platform", new[] { "radeon.0", "temp", "1" }, TestHelpers.FakeSnapshot(), _settings));
        Assert.Equal("30", VariableEvaluator.Evaluate("platform", new[] { "disk.0", "temp", "1" }, TestHelpers.FakeSnapshot(), _settings));
    }

    [Fact]
    public void Platform_NonTempField_ReturnsNull()
    {
        Assert.Null(VariableEvaluator.Evaluate("platform", new[] { "coretemp.0", "fan", "1" }, TestHelpers.FakeSnapshot(), _settings));
    }

    [Fact]
    public void Hddtemp_MapsToFirstDiskSensor()
    {
        Assert.Equal("30", VariableEvaluator.Evaluate("hddtemp", new[] { "/dev/sda" }, TestHelpers.FakeSnapshot(), _settings));
    }

    [Fact]
    public void CpuN_UsesPerCoreWhenInRange()
    {
        Assert.Equal("20", VariableEvaluator.Evaluate("cpu", new[] { "2" }, TestHelpers.FakeSnapshot(), _settings));
        Assert.Equal("12", VariableEvaluator.Evaluate("cpu", new[] { "99" }, TestHelpers.FakeSnapshot(), _settings)); // 越界回退总占用
        Assert.Equal("12", VariableEvaluator.Evaluate("cpu", Array.Empty<string>(), TestHelpers.FakeSnapshot(), _settings));
    }
}