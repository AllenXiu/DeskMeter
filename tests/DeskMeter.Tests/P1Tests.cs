using DeskMeter.Core.Config;
using DeskMeter.Core.Objects;
using Xunit;

namespace DeskMeter.Tests;

public class P1ScrollTests
{
    private readonly ObjectRegistry _registry = new();
    private readonly ConfigSettings _settings = TestHelpers.Settings();

    [Fact]
    public void Render_Scroll_ShortTextIsStatic()
    {
        var nodes = ConkyTextParser.Parse("${scroll 32 short text}", _registry, _settings);
        var layout = Render(nodes);
        Assert.Equal("short text", layout.Lines[0].PlainText);
    }

    [Fact]
    public void Render_Scroll_AdvancesWindowEachRefresh()
    {
        // show=4：前缀补 4 空格后左移，每帧前进 1 字符
        var nodes = ConkyTextParser.Parse("${scroll 4 abcdefgh}", _registry, _settings);
        Assert.Equal("   a", Render(nodes).Lines[0].PlainText);
        Assert.Equal("  ab", Render(nodes).Lines[0].PlainText);
        Assert.Equal(" abc", Render(nodes).Lines[0].PlainText);
    }

    private static WidgetLayout Render(System.Collections.Generic.List<ObjectNode> nodes)
    {
        var layout = new WidgetLayout();
        var ctx = new RenderContext(TestHelpers.FakeSnapshot(), TestHelpers.Settings(), layout);
        foreach (var n in nodes) n.Print(ctx);
        return layout;
    }
}

public class P1ExecTests
{
    private readonly ObjectRegistry _registry = new();
    private readonly ConfigSettings _settings = TestHelpers.Settings();

    [Fact]
    public async Task Render_Exec_AsyncPopulatesOutput()
    {
        var nodes = ConkyTextParser.Parse("${exec echo hello}", _registry, _settings);
        var text = string.Empty;
        for (var i = 0; i < 100; i++)
        {
            var layout = Render(nodes);
            text = layout.ToConsoleText().Trim();
            if (text != "--") break;
            await Task.Delay(50);
        }
        Assert.Equal("hello", text);
    }

    [Fact]
    public void Render_Exec_FirstPrintShowsPlaceholder()
    {
        var nodes = ConkyTextParser.Parse("${exec ping -n 1 127.0.0.1 >nul}", _registry, _settings);
        var layout = Render(nodes);
        Assert.Equal("--", layout.ToConsoleText().Trim());
    }

    private static WidgetLayout Render(System.Collections.Generic.List<ObjectNode> nodes)
    {
        var layout = new WidgetLayout();
        var ctx = new RenderContext(TestHelpers.FakeSnapshot(), TestHelpers.Settings(), layout);
        foreach (var n in nodes) n.Print(ctx);
        return layout;
    }
}

public class P1AlignTests
{
    private readonly ObjectRegistry _registry = new();
    private readonly ConfigSettings _settings = TestHelpers.Settings();

    [Fact]
    public void Render_AlignR_EmitsElementWithGap()
    {
        var nodes = ConkyTextParser.Parse("left${alignr 10}right", _registry, _settings);
        var layout = Render(nodes);
        Assert.IsType<WidgetText>(layout.Lines[0].Elements[0]);
        var align = Assert.IsType<WidgetAlignR>(layout.Lines[0].Elements[1]);
        Assert.Equal(10, align.N);
        Assert.IsType<WidgetText>(layout.Lines[0].Elements[2]);
    }

    [Fact]
    public void Render_AlignC_EmitsElement()
    {
        var nodes = ConkyTextParser.Parse("${alignc}center", _registry, _settings);
        var layout = Render(nodes);
        Assert.IsType<WidgetAlignC>(layout.Lines[0].Elements[0]);
        Assert.IsType<WidgetText>(layout.Lines[0].Elements[1]);
    }

    [Fact]
    public void Console_IgnoresAlignObjects_LikeConky()
    {
        var nodes = ConkyTextParser.Parse("left${alignr 10}right", _registry, _settings);
        var layout = Render(nodes);
        Assert.Equal("leftright", layout.ToConsoleText().Trim());
    }

    private static WidgetLayout Render(System.Collections.Generic.List<ObjectNode> nodes)
    {
        var layout = new WidgetLayout();
        var ctx = new RenderContext(TestHelpers.FakeSnapshot(), TestHelpers.Settings(), layout);
        foreach (var n in nodes) n.Print(ctx);
        return layout;
    }
}

public class P1NamedColorTests
{
    [Fact]
    public void ColorParser_FullX11Set_ExoticNames()
    {
        Assert.Equal(new WidgetBrush(0xFF, 0xDA, 0xB9), ColorParser.Parse("peachpuff"));
        Assert.Equal(new WidgetBrush(0x64, 0x95, 0xED), ColorParser.Parse("cornflowerblue"));
        Assert.Equal(new WidgetBrush(0xFF, 0xDE, 0xAD), ColorParser.Parse("navajowhite"));
    }

    [Fact]
    public void ColorParser_GreyAliases_BothSpellings()
    {
        Assert.Equal(ColorParser.Parse("gray"), ColorParser.Parse("grey"));
        Assert.Equal(ColorParser.Parse("lightgray"), ColorParser.Parse("lightgrey"));
        Assert.Equal(ColorParser.Parse("darkslategray"), ColorParser.Parse("darkslategrey"));
    }

    [Fact]
    public void NamedColors_HasHundredsOfEntries()
    {
        Assert.True(NamedColors.TryGet("rebeccapurple") is not null);
        Assert.True(NamedColors.TryGet("mediumspringgreen") is not null);
        Assert.True(NamedColors.TryGet("oldlace") is not null);
    }
}
