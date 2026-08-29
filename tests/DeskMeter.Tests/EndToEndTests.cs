using DeskMeter.Core.Data;
using DeskMeter.Core.Objects;
using Xunit;

namespace DeskMeter.Tests;

public class EndToEndTests
{
    [Fact]
    public void SampleConfig_LoadsAndRenders()
    {
        var path = Path.Combine(TestHelpers.FindRepoRoot(), "samples", "conky.conf");
        var engine = new Core.Config.LuaConfigEngine();
        var config = engine.LoadFile(path);

        // 用户已重写 samples/conky.conf 为官方风格（top_left / update_interval=1）
        Assert.Equal(Core.Config.WidgetAlignment.TopLeft, config.Settings.GetAlignment());
        Assert.Equal(1.0, config.Settings.GetUpdateInterval());

        var registry = new ObjectRegistry();
        var nodes = ConkyTextParser.Parse(config.Text, registry, config.Settings);
        Assert.NotEmpty(nodes);

        var layout = new WidgetLayout();
        var ctx = new RenderContext(TestHelpers.FakeSnapshot(), config.Settings, layout);
        foreach (var n in nodes) n.Print(ctx);

        var text = layout.ToConsoleText();
        Assert.Contains("Uptime: 3d 4h 12m", text); // FakeSnapshot 的 uptime
        Assert.DoesNotContain("$hr", text); // $hr 已解析为分隔线而非字面量
        Assert.True(layout.Lines.Any(l => l.IsRule), "应有分隔线行");
    }
}
