using DeskMeter.Core.Config;
using DeskMeter.Core.Data;
using DeskMeter.Core.Objects;
using Xunit;

namespace DeskMeter.Tests;

public class P2TopTests
{
    private readonly ObjectRegistry _registry = new();
    private readonly ConfigSettings _settings = TestHelpers.Settings();

    [Fact]
    public void Top_CpuList_FieldsByNamePidCpuMem()
    {
        Assert.Equal("firefox".PadRight(15), VariableEvaluator.Evaluate("top", new[] { "name", "1" }, TestHelpers.FakeSnapshot(), _settings));
        Assert.Equal("4821".PadLeft(7), VariableEvaluator.Evaluate("top", new[] { "pid", "1" }, TestHelpers.FakeSnapshot(), _settings));
        Assert.Equal("12.40".PadLeft(6), VariableEvaluator.Evaluate("top", new[] { "cpu", "1" }, TestHelpers.FakeSnapshot(), _settings));
        Assert.Equal("8.20".PadLeft(6), VariableEvaluator.Evaluate("top", new[] { "mem", "1" }, TestHelpers.FakeSnapshot(), _settings));
        Assert.Equal("3.00".PadLeft(6), VariableEvaluator.Evaluate("top", new[] { "mem", "2" }, TestHelpers.FakeSnapshot(), _settings));
    }

    [Fact]
    public void Top_LongName_TruncatedToColumnWidth()
    {
        // 20 字符长进程名：截断到 top_name_width（默认 15），防止顶破列对齐
        var longName = new string('x', 20);
        var snap = new DeskMeter.Core.Data.SystemSnapshot
        {
            TopCpu = new[] { new DeskMeter.Core.Data.ProcessInfo(longName, 1, 10, 5) },
        };
        var value = VariableEvaluator.Evaluate("top", new[] { "name", "1" }, snap, _settings);
        Assert.Equal(15, value!.Length);
        Assert.Equal(new string('x', 15), value);
    }

    [Fact]
    public void Top_OutOfRange_ReturnsNullPlaceholder()
    {
        Assert.Null(VariableEvaluator.Evaluate("top", new[] { "name", "99" }, TestHelpers.FakeSnapshot(), _settings));
    }

    [Fact]
    public void TopMem_MemoryList_Fields()
    {
        Assert.Equal("chrome".PadRight(15), VariableEvaluator.Evaluate("top_mem", new[] { "name", "1" }, TestHelpers.FakeSnapshot(), _settings));
        Assert.Equal("20.10".PadLeft(6), VariableEvaluator.Evaluate("top_mem", new[] { "mem", "1" }, TestHelpers.FakeSnapshot(), _settings));
    }

    [Fact]
    public void Render_OfficialTopLine_ProducesValues()
    {
        // 官方默认配置 top 行：${top name 1} ${top pid 1} ${top cpu 1} ${top mem 1}
        var nodes = ConkyTextParser.Parse("${top name 1} ${top pid 1} ${top cpu 1} ${top mem 1}", _registry, _settings);
        var layout = new WidgetLayout();
        var ctx = new RenderContext(TestHelpers.FakeSnapshot(), _settings, layout);
        foreach (var n in nodes) n.Print(ctx);
        var expected = "firefox".PadRight(15) + " " + "4821".PadLeft(7) + " " + "12.40".PadLeft(6) + " " + "8.20".PadLeft(6);
        Assert.Equal(expected, layout.Lines[0].PlainText);
    }
}