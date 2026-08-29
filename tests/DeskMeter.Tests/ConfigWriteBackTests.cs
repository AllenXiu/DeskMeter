using DeskMeter.Core.Config;
using Xunit;

namespace DeskMeter.Tests;

public class ConfigWriteBackTests
{
    [Fact]
    public void SetUpdateInterval_ReplacesExisting()
    {
        const string src = "conky.config = {\n    update_interval = 2,\n    alignment = 'top_right',\n};\nconky.text = [[x]];\n";
        var result = ConfigWriteBack.SetUpdateInterval(src, 5.5);
        Assert.Contains("update_interval = 5.5,", result);
        Assert.DoesNotContain("update_interval = 2,", result);
    }

    [Fact]
    public void SetUpdateInterval_InsertsWhenMissing()
    {
        const string src = "conky.config = {\n    alignment = 'top_right',\n};\nconky.text = [[x]];\n";
        var result = ConfigWriteBack.SetUpdateInterval(src, 3);
        Assert.Contains("update_interval = 3,", result);
        Assert.Contains("conky.config = {\n    update_interval = 3,", result);
    }

    [Fact]
    public void SetDeskmeterValue_CreatesBlockWhenMissing()
    {
        const string src = "conky.config = { update_interval = 2 };\nconky.text = [[x]];\n";
        var result = ConfigWriteBack.SetDeskmeterValue(src, "click_through", "false");
        Assert.Contains("deskmeter = { click_through = false };", result);
    }

    [Fact]
    public void SetDeskmeterValue_UpdatesExistingBlockKey()
    {
        const string src = "deskmeter = { monitor = 1, click_through = true };\nconky.text = [[x]];\n";
        var result = ConfigWriteBack.SetDeskmeterValue(src, "monitor", "2");
        Assert.Contains("monitor = 2", result);
        Assert.DoesNotContain("monitor = 1", result);
    }

    [Fact]
    public void SetDeskmeterValue_AddsKeyToExistingBlock()
    {
        const string src = "deskmeter = { monitor = 1 };\nconky.text = [[x]];\n";
        var result = ConfigWriteBack.SetDeskmeterValue(src, "click_through", "false");
        Assert.Contains("click_through = false", result);
        Assert.Contains("monitor = 1", result);
    }

    [Fact]
    public void Update_CombinesAllValues()
    {
        const string src = "conky.config = { update_interval = 2, alignment = 'top_right' };\ndeskmeter = { monitor = 1 };\nconky.text = [[x]];\n";
        var result = ConfigWriteBack.Update(src, 4, false, 3);
        Assert.Contains("update_interval = 4", result);
        Assert.Contains("click_through = false", result);
        Assert.Contains("monitor = 3", result);
    }
}