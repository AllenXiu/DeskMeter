using DeskMeter.Core.Config;
using DeskMeter.Core.Data;
using DeskMeter.Core.Objects;
using Xunit;

namespace DeskMeter.Tests;

/// <summary>P2 边界项：行内 ${font}、scroll 方向、graph 旗标、offset/voffset/tab 像素语义。</summary>
public class P2BoundaryTests
{
    private readonly ObjectRegistry _registry = new();
    private readonly ConfigSettings _settings = TestHelpers.Settings();

    private WidgetLayout Render(string conkyText, SystemSnapshot? snap = null)
    {
        var layout = new WidgetLayout();
        var ctx = new RenderContext(snap ?? TestHelpers.FakeSnapshot(), _settings, layout);
        foreach (var node in ConkyTextParser.Parse(conkyText, _registry, _settings)) node.Print(ctx);
        return layout;
    }

    // ---- 行内 ${font} ----

    [Fact]
    public void FontSpec_Parse_FamilyAndSize()
    {
        var f = FontSpec.Parse("Consolas:size=14");
        Assert.NotNull(f);
        Assert.Equal("Consolas", f!.Value.Family);
        Assert.Equal(14, f.Value.Size);
    }

    [Fact]
    public void FontSpec_Parse_FamilyOnly_SizeZero()
    {
        var f = FontSpec.Parse("Consolas");
        Assert.Equal("Consolas", f!.Value.Family);
        Assert.Equal(0, f.Value.Size);
    }

    [Fact]
    public void FontSpec_Parse_Invalid_ReturnsNull()
    {
        Assert.Null(FontSpec.Parse(null));
        Assert.Null(FontSpec.Parse(""));
        Assert.Null(FontSpec.Parse("  "));
    }

    [Fact]
    public void InlineFont_SetsCurrentFontOnText()
    {
        var layout = Render("${font Consolas:size=14}hello");
        var text = Assert.IsType<WidgetText>(layout.Lines[0].Elements[0]);
        Assert.Equal("Consolas", text.Font!.Value.Family);
        Assert.Equal(14, text.Font.Value.Size);
    }

    [Fact]
    public void InlineFont_Reset_ReturnsToNull()
    {
        var layout = Render("${font Consolas:size=14}a${font}b");
        Assert.Equal("Consolas", Assert.IsType<WidgetText>(layout.Lines[0].Elements[0]).Font!.Value.Family);
        Assert.Null(Assert.IsType<WidgetText>(layout.Lines[0].Elements[1]).Font);
    }

    // ---- scroll 方向 ----

    [Fact]
    public void Scroll_Left_Default_AdvancesLeft()
    {
        var nodes = ConkyTextParser.Parse("${scroll 5 1 ABCDEFGH}", _registry, _settings);
        _ = RenderOnce(nodes, TestHelpers.FakeSnapshot());
        _ = RenderOnce(nodes, TestHelpers.FakeSnapshot());
        var s3 = RenderOnce(nodes, TestHelpers.FakeSnapshot());
        Assert.Equal("  ABC", s3); // 第 3 次刷新：左移窗口显示 ABC 前缀两空格
    }

    [Fact]
    public void Scroll_Right_Direction_ShowsTailFirst()
    {
        var nodes = ConkyTextParser.Parse("${scroll right 5 1 ABCDEFGH}", _registry, _settings);
        _ = RenderOnce(nodes, TestHelpers.FakeSnapshot());
        _ = RenderOnce(nodes, TestHelpers.FakeSnapshot());
        var s3 = RenderOnce(nodes, TestHelpers.FakeSnapshot());
        Assert.Equal("DEFGH", s3); // 右向：从尾部开始
    }

    [Fact]
    public void Scroll_Wait_HoldsAtEnd_NoWrap()
    {
        var nodes = ConkyTextParser.Parse("${scroll wait 5 3 ABCDEFGH}", _registry, _settings);
        _ = RenderOnce(nodes, TestHelpers.FakeSnapshot());
        _ = RenderOnce(nodes, TestHelpers.FakeSnapshot());
        _ = RenderOnce(nodes, TestHelpers.FakeSnapshot());
        var s4 = RenderOnce(nodes, TestHelpers.FakeSnapshot());
        var s5 = RenderOnce(nodes, TestHelpers.FakeSnapshot());
        Assert.Equal("DEFGH", s4);
        Assert.Equal("DEFGH", s5); // wait：到尾后停留，不回绕
    }

    private string RenderOnce(List<ObjectNode> nodes, SystemSnapshot snap)
    {
        var layout = new WidgetLayout();
        var ctx = new RenderContext(snap, _settings, layout);
        foreach (var node in nodes) node.Print(ctx);
        return layout.ToConsoleText().TrimEnd('\r', '\n');
    }

    // ---- graph 旗标 ----

    [Fact]
    public void Graph_Flags_LogMaxIntervalYScale()
    {
        var snap = new SystemSnapshot { CpuPercent = 12 };
        var layout = Render("${cpugraph -l -m 100 -i 2 -y 2 10,50}", snap);
        var graph = Assert.IsType<WidgetGraph>(layout.Lines[0].Elements[0]);
        Assert.True(graph.LogScale);
        Assert.Equal(100, graph.MaxOverride);
        Assert.Equal(50, graph.Width);
        Assert.Equal(10, graph.Height);
        Assert.Single(graph.Series);
        Assert.Equal(24, graph.Series[0]); // 12 * yScale(2)
    }

    [Fact]
    public void Graph_NoFlags_Defaults()
    {
        var snap = new SystemSnapshot { CpuPercent = 12 };
        var layout = Render("${cpugraph 10,50}", snap);
        var graph = Assert.IsType<WidgetGraph>(layout.Lines[0].Elements[0]);
        Assert.False(graph.LogScale);
        Assert.Null(graph.MaxOverride);
        Assert.Single(graph.Series);
        Assert.Equal(12, graph.Series[0]);
    }

    // ---- offset / voffset / tab ----

    [Fact]
    public void Layout_OffsetVOffsetTab_ProducePixelElements()
    {
        var layout = Render("${offset 20}A${voffset 5}B${tab 30}C");
        var els = layout.Lines[0].Elements;
        Assert.IsType<WidgetOffset>(els[0]);
        Assert.Equal(20, Assert.IsType<WidgetOffset>(els[0]).N);
        Assert.Equal("A", Assert.IsType<WidgetText>(els[1]).Text);
        Assert.Equal(5, Assert.IsType<WidgetVOffset>(els[2]).N);
        Assert.Equal("B", Assert.IsType<WidgetText>(els[3]).Text);
        Assert.Equal(30, Assert.IsType<WidgetTab>(els[4]).N);
        Assert.Equal("C", Assert.IsType<WidgetText>(els[5]).Text);
    }

    [Fact]
    public void Layout_NegativeOffset_Allowed()
    {
        var layout = Render("${offset -10}A");
        Assert.Equal(-10, Assert.IsType<WidgetOffset>(layout.Lines[0].Elements[0]).N);
    }
}