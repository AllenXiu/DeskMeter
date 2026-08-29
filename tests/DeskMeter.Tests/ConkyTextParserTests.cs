using DeskMeter.Core.Config;
using DeskMeter.Core.Objects;
using Xunit;

namespace DeskMeter.Tests;

public class ConkyTextParserTests
{
    private readonly ObjectRegistry _registry = new();
    private readonly ConfigSettings _settings = TestHelpers.Settings();

    [Fact]
    public void Parse_PlainText_SingleTextNode()
    {
        var nodes = ConkyTextParser.Parse("hello", _registry, _settings);
        var node = Assert.Single(nodes);
        Assert.IsType<TextNode>(node);
    }

    [Fact]
    public void Parse_VariableAndArgs_ProducesExpectedNodes()
    {
        var nodes = ConkyTextParser.Parse("$cpu ${membar 4} x", _registry, _settings);
        Assert.Equal(4, nodes.Count);
        Assert.IsType<VariableNode>(nodes[0]);
        Assert.IsType<TextNode>(nodes[1]);
        Assert.IsType<BarNode>(nodes[2]);
        Assert.IsType<TextNode>(nodes[3]);
    }

    [Fact]
    public void Parse_DollarEscape_IsLiteralDollar()
    {
        var nodes = ConkyTextParser.Parse("price $$5", _registry, _settings);
        var layout = Render(nodes);
        Assert.Equal("price $5", layout.Lines[0].PlainText);
    }

    [Fact]
    public void Parse_BackslashN_IsNewline()
    {
        var nodes = ConkyTextParser.Parse("a\nb", _registry, _settings);
        var layout = Render(nodes);
        Assert.Equal(2, layout.Lines.Count);
        Assert.Equal("a", layout.Lines[0].PlainText);
        Assert.Equal("b", layout.Lines[1].PlainText);
    }

    [Fact]
    public void Parse_CaseInsensitive_ResolvesSameVariable()
    {
        var nodes = ConkyTextParser.Parse("${CPU} ${MemMax}", _registry, _settings);
        Assert.IsType<VariableNode>(nodes[0]);
        Assert.IsType<TextNode>(nodes[1]); // 中间的空格是文本节点
        Assert.IsType<VariableNode>(nodes[2]);
    }

    [Fact]
    public void Parse_ColorPaletteVariable_ProducesColorNode()
    {
        var nodes = ConkyTextParser.Parse("$color0 text", _registry, _settings);
        Assert.IsType<ColorNode>(nodes[0]);
        Assert.IsType<TextNode>(nodes[1]);
    }

    [Fact]
    public void Render_Goto_EmitsPixelPositionElement()
    {
        var nodes = ConkyTextParser.Parse("${goto 110}x", _registry, _settings);
        var layout = Render(nodes);
        var gotoEl = Assert.IsType<WidgetGoto>(layout.Lines[0].Elements[0]);
        Assert.Equal(110, gotoEl.X);
        Assert.IsType<WidgetText>(layout.Lines[0].Elements[1]);
    }

    [Fact]
    public void Console_IgnoresGoto_LikeConky()
    {
        var nodes = ConkyTextParser.Parse("${goto 110}${membar 4}", _registry, _settings);
        var layout = Render(nodes);
        var text = layout.ToConsoleText().TrimEnd();
        Assert.Equal(10, text.Length); // 无空格：Conky console 后端忽略 goto
    }

    [Fact]
    public void Render_CpuGraph_EmitsWidgetGraphElement()
    {
        var nodes = ConkyTextParser.Parse("${cpugraph 32,260}", _registry, _settings);
        var layout = Render(nodes);
        var graph = Assert.IsType<WidgetGraph>(Assert.Single(layout.Lines[0].Elements));
        Assert.Equal(32, graph.Height);
        Assert.Equal(260, graph.Width);
        var sample = Assert.Single(graph.Series);
        Assert.Equal(12, sample); // FakeSnapshot.CpuPercent = 12
    }

    [Fact]
    public void Render_CpuGraph_AccumulatesHistoryAcrossRefreshes()
    {
        var nodes = ConkyTextParser.Parse("${cpugraph 32}", _registry, _settings);
        _ = Render(nodes); // 第 1 次采样
        var layout = Render(nodes); // 第 2 次采样（同一节点实例保留历史）
        var graph = Assert.IsType<WidgetGraph>(Assert.Single(layout.Lines[0].Elements));
        Assert.Equal(2, graph.Series.Count);
        Assert.Equal(0, graph.Width); // 宽度省略 → 填满剩余
        Assert.Equal(32, graph.Height);
    }

    [Fact]
    public void Render_GraphConsoleFallback_UsesConkyTicks()
    {
        var nodes = ConkyTextParser.Parse("${cpugraph 4}", _registry, _settings);
        _ = Render(nodes);
        var layout = Render(nodes);
        var text = layout.ToConsoleText().TrimEnd();
        Assert.NotEmpty(text);
        Assert.All(text, c => Assert.Contains(c, " ,_,=#")); // Conky console_graph_ticks
    }

    [Fact]
    public void Render_HrFollowedByNewline_NoBlankLine()
    {
        // $hr\n：规则行占一行，紧跟的换行不应产生空行（Conky 行为）
        var nodes = ConkyTextParser.Parse("a$hr\nb", _registry, _settings);
        var layout = Render(nodes);
        Assert.Equal(3, layout.Lines.Count);
        Assert.True(layout.Lines[1].IsRule);
        Assert.Equal("b", layout.Lines[2].PlainText);
    }

    [Fact]
    public void Render_HrAfterNewline_ConvertsEmptyLine_NoBlank()
    {
        // "a\n$hr\nb"：换行后的空行被 $hr 占用，不产生空行
        var nodes = ConkyTextParser.Parse("a\n$hr\nb", _registry, _settings);
        var layout = Render(nodes);
        Assert.Equal(3, layout.Lines.Count);
        Assert.True(layout.Lines[1].IsRule);
        Assert.Equal("b", layout.Lines[2].PlainText);
    }

    [Fact]
    public void Render_UnknownVariable_ShowsPlaceholder()
    {
        var nodes = ConkyTextParser.Parse("$totally_unknown_xyz", _registry, _settings);
        var layout = Render(nodes);
        Assert.Equal("--", layout.Lines[0].PlainText);
    }

    [Fact]
    public void Render_FakeSnapshot_ValuesAppear()
    {
        var nodes = ConkyTextParser.Parse(
            "$hostname $cpu% $memperc% $uptime $freq $downspeed",
            _registry, _settings);
        var layout = Render(nodes);
        var text = layout.ToConsoleText();
        Assert.Contains("DESKTOP-ABC123", text);
        Assert.Contains("12%", text);   // $cpu%：变量输出 12 + 字面 %
        Assert.Contains("26%", text);   // 4.2/16 GiB = 26.25 → 26
        Assert.Contains("3d 4h 12m", text);
        Assert.Contains("3600", text);
        Assert.Contains("2.4MiB/s", text);
    }

    [Fact]
    public void Render_BarNode_EmitsVectorBarElement()
    {
        var nodes = ConkyTextParser.Parse("${membar 4,120}", _registry, _settings);
        var layout = Render(nodes);
        var bar = Assert.IsType<WidgetBar>(Assert.Single(layout.Lines[0].Elements));
        Assert.Equal(4, bar.Height);      // Conky：高度[,宽度]
        Assert.Equal(120, bar.Width);
        Assert.InRange(bar.Percent, 26, 27); // 4.2/16 GiB = 26.25%
    }

    [Fact]
    public void Render_BarNode_NoWidth_FillsRemainingLine()
    {
        // Conky 语义：省略宽度 = 0 = 填满本行剩余宽度
        var nodes = ConkyTextParser.Parse("${cpubar 6}", _registry, _settings);
        var layout = Render(nodes);
        var bar = Assert.IsType<WidgetBar>(Assert.Single(layout.Lines[0].Elements));
        Assert.Equal(6, bar.Height);
        Assert.Equal(0, bar.Width);
    }

    [Fact]
    public void Render_BarNode_ConsoleFallback_UsesHashAndDot()
    {
        // Console 后端回退：Conky console 风格（# 填充 / . 未填充，宽度=像素宽）
        var nodes = ConkyTextParser.Parse("${membar 4,120}", _registry, _settings);
        var layout = Render(nodes);
        var text = layout.Lines[0].PlainText.Length == 0 ? layout.ToConsoleText().TrimEnd() : "";
        Assert.Equal(120, text.Length);
        Assert.Contains("#", text);
        Assert.Contains(".", text);
    }

    private static WidgetLayout Render(System.Collections.Generic.List<ObjectNode> nodes)
    {
        var layout = new WidgetLayout();
        var ctx = new RenderContext(TestHelpers.FakeSnapshot(), TestHelpers.Settings(), layout);
        foreach (var n in nodes) n.Print(ctx);
        return layout;
    }
}
