using DeskMeter.Core.Objects;
using Xunit;

namespace DeskMeter.Tests;

public class WidgetMetricsTests
{
    [Theory]
    [InlineData(100, 0, 0, 100)]    // 无约束
    [InlineData(100, 200, 0, 200)]  // minimum 下限
    [InlineData(300, 0, 260, 260)]  // maximum 上限
    [InlineData(100, 200, 260, 200)]// 上下限之间取内容
    [InlineData(300, 200, 260, 260)]// 超上限时上限优先
    public void ClampWidth_ConkyTextSizeSemantics(double content, double min, double max, double expected)
    {
        Assert.Equal(expected, WidgetMetrics.ClampWidth(content, min, max));
    }
}

public class ConfigSettingsTests
{
    [Fact]
    public void GetMinimumSize_NumberForm_WidthEqualsHeight()
    {
        var settings = TestHelpers.Settings(new Dictionary<string, object?> { ["minimum_size"] = 200.0 });
        var (w, h) = settings.GetMinimumSize();
        Assert.Equal(200, w);
        Assert.Equal(200, h);
    }

    [Fact]
    public void GetMinimumSize_StringForm_ParsesWidthHeight()
    {
        var settings = TestHelpers.Settings(new Dictionary<string, object?> { ["minimum_size"] = "200,100" });
        var (w, h) = settings.GetMinimumSize();
        Assert.Equal(200, w);
        Assert.Equal(100, h);
    }

    [Fact]
    public void GetMinimumSize_Missing_ReturnsZero()
    {
        var (w, h) = TestHelpers.Settings().GetMinimumSize();
        Assert.Equal(0, w);
        Assert.Equal(0, h);
    }
}

public class UseSpacerTests
{
    [Fact]
    public void Evaluate_Mem_WithRightSpacer_PadsRightTo7()
    {
        var settings = TestHelpers.Settings(new Dictionary<string, object?> { ["use_spacer"] = "right" });
        var value = VariableEvaluator.Evaluate("mem", Array.Empty<string>(), TestHelpers.FakeSnapshot(), settings);
        Assert.Equal("4.2GiB ", value); // 6 字符补齐到 7
    }

    [Fact]
    public void Evaluate_Mem_WithLeftSpacer_PadsLeftTo7()
    {
        var settings = TestHelpers.Settings(new Dictionary<string, object?> { ["use_spacer"] = "left" });
        var value = VariableEvaluator.Evaluate("mem", Array.Empty<string>(), TestHelpers.FakeSnapshot(), settings);
        Assert.Equal(" 4.2GiB", value);
    }

    [Fact]
    public void Evaluate_Cpu_WithRightSpacer_PadsPercentTo3()
    {
        var settings = TestHelpers.Settings(new Dictionary<string, object?> { ["use_spacer"] = "right" });
        var value = VariableEvaluator.Evaluate("cpu", Array.Empty<string>(), TestHelpers.FakeSnapshot(), settings);
        Assert.Equal("12 ", value);
    }

    [Fact]
    public void Evaluate_Mem_NoSpacer_Unchanged()
    {
        var value = VariableEvaluator.Evaluate("mem", Array.Empty<string>(), TestHelpers.FakeSnapshot(), TestHelpers.Settings());
        Assert.Equal("4.2GiB", value);
    }
}
