using DeskMeter.Core.Config;
using Xunit;

namespace DeskMeter.Tests;

public class ConfigManagerTests : IDisposable
{
    private readonly string _dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dmcfg_" + Guid.NewGuid().ToString("N"));
    private readonly string _srcDir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dmcfgsrc_" + Guid.NewGuid().ToString("N"));
    private readonly ConfigManager _manager;

    public ConfigManagerTests()
    {
        Directory.CreateDirectory(_dir);
        Directory.CreateDirectory(_srcDir);
        _manager = new ConfigManager(_dir);
    }

    private string WriteConf(string name, string content)
    {
        var p = System.IO.Path.Combine(_srcDir, name + ".conf");
        System.IO.File.WriteAllText(p, content);
        return p;
    }

    [Fact]
    public void Import_CopiesAndNames()
    {
        var src = WriteConf("src", "conky.config={}\n");
        var entry = _manager.Import(src, "我的配置");
        Assert.NotNull(entry);
        Assert.Equal("我的配置", entry!.Name);
        Assert.True(System.IO.File.Exists(entry.Path));
        Assert.Single(_manager.List());
    }

    [Fact]
    public void Import_Collision_AppendsSuffix()
    {
        var src = WriteConf("src", "x");
        _manager.Import(src, "a");
        var second = _manager.Import(src, "a");
        Assert.Equal("a (2)", second!.Name);
    }

    [Fact]
    public void SetCurrent_And_Current_RoundTrip()
    {
        var src = WriteConf("src", "x");
        var entry = _manager.Import(src, "b");
        Assert.True(_manager.SetCurrent(entry!));
        Assert.Equal("b", _manager.Current()!.Name);
    }

    [Fact]
    public void Rename_ChangesNameAndFile()
    {
        var src = WriteConf("src", "x");
        var entry = _manager.Import(src, "old");
        Assert.True(_manager.Rename(entry!, "new"));
        Assert.True(System.IO.File.Exists(System.IO.Path.Combine(_dir, "new.conf")));
        Assert.Equal("new", _manager.List()[0].Name);
    }

    [Fact]
    public void Delete_RemovesEntry_AndClearsCurrent()
    {
        var src = WriteConf("src", "x");
        var entry = _manager.Import(src, "c");
        _manager.SetCurrent(entry!);
        Assert.True(_manager.Delete(entry!));
        Assert.Empty(_manager.List());
        Assert.Null(_manager.Current());
    }

    [Fact]
    public void EnsureDefault_ImportsSampleOnEmpty()
    {
        var sample = WriteConf("sample", "conky.config={}\n");
        var entry = _manager.EnsureDefault(sample);
        Assert.NotNull(entry);
        Assert.Equal("默认", entry!.Name);
        Assert.Equal("默认", _manager.Current()!.Name);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, true); } catch { }
        try { Directory.Delete(_srcDir, true); } catch { }
    }
}