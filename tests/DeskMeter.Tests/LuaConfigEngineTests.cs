using DeskMeter.Core.Config;
using Xunit;

namespace DeskMeter.Tests;

public class LuaConfigEngineTests
{
    private readonly LuaConfigEngine _engine = new();

    [Fact]
    public void Parse_OfficialConkyConfig_Succeeds()
    {
        // conky-main/ 是 gitignore 的本地参考源码（仅开发机存在），CI 检出时没有该文件 → 跳过
        var path = Path.Combine(TestHelpers.FindRepoRoot(), "conky-main", "data", "conky.conf");
        if (!System.IO.File.Exists(path)) return;

        var config = _engine.LoadFile(path);

        Assert.Equal("top_left", config.Settings.GetString("alignment"));
        Assert.Equal(1.0, config.Settings.GetNumber("update_interval"));
        Assert.False(config.Settings.GetBool("draw_borders"));
        Assert.NotNull(config.Text);
        Assert.Contains("$mem", config.Text);
    }

    [Fact]
    public void Parse_SupportsLuaFunctionsAndComputation()
    {
        const string lua = """
            function getInterval() return 3 end
            conky.config = { update_interval = getInterval() + 1, alignment = 'bottom_right' }
            conky.text = [[hello $cpu]]
            """;
        var config = _engine.Parse(lua);

        Assert.Equal(4.0, config.Settings.GetNumber("update_interval"));
        Assert.Equal("bottom_right", config.Settings.GetString("alignment"));
        Assert.Equal("hello $cpu", config.Text);
    }

    [Fact]
    public void Parse_UnknownKeysIgnored()
    {
        const string lua = """
            conky.config = { update_interval = 2, some_future_key = 'x', nested = { a = 1 } }
            conky.text = [[ok]]
            """;
        var config = _engine.Parse(lua);
        Assert.Equal(2.0, config.Settings.GetNumber("update_interval"));
        Assert.Equal("ok", config.Text);
    }

    [Fact]
    public void Parse_NoConkyTable_ReturnsEmptyConfig()
    {
        // 引擎预注册 conky 全局表（与 Conky 相同），无 conky.config/text 时宽松返回空配置
        var config = _engine.Parse("local x = 1");
        Assert.Empty(config.Settings.Values);
        Assert.Equal(string.Empty, config.Text);
    }

    [Fact]
    public void Parse_InvalidLua_Throws()
    {
        Assert.Throws<ConkyConfigException>(() => _engine.Parse("conky.config = {"));
    }

    [Fact]
    public void Parse_DeskmeterExtensionBlock_MergesKeys()
    {
        const string lua = """
            conky.config = { update_interval = 2 }
            conky.text = [[x]]
            deskmeter = { monitor = 2, click_through = true }
            """;
        var config = _engine.Parse(lua);
        Assert.Equal(2.0, config.Settings.GetNumber("monitor"));
        Assert.True(config.Settings.GetBool("click_through"));
    }

    [Fact]
    public void Settings_GetAlignment_ParsesAbbreviations()
    {
        var cfg = TestHelpers.Settings(new Dictionary<string, object?> { ["alignment"] = "tr" });
        Assert.Equal(WidgetAlignment.TopRight, cfg.GetAlignment());
    }
}
