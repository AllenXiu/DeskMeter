using DeskMeter.Core.Config;
using DeskMeter.Core.Data;
using DeskMeter.Core.Objects;
using Xunit;

namespace DeskMeter.Tests;

/// <summary>Conky 变量扩展（Windows 替代）：if_* 条件块、网络信息、loadavg。</summary>
public class P2WindowsCompatTests
{
    private readonly ObjectRegistry _registry = new();
    private readonly ConfigSettings _settings = TestHelpers.Settings();

    private string Render(string conkyText, SystemSnapshot? snap = null, int updateNumber = 0)
    {
        var layout = new WidgetLayout();
        var ctx = new RenderContext(snap ?? TestHelpers.FakeSnapshot(), _settings, layout) { UpdateNumber = updateNumber };
        foreach (var node in ConkyTextParser.Parse(conkyText, _registry, _settings)) node.Print(ctx);
        return layout.ToConsoleText().TrimEnd('\r', '\n');
    }

    // ---- if_* 条件块 ----

    [Fact]
    public void IfExisting_TrueBranch_WhenFileExists()
    {
        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dm_if_" + Guid.NewGuid().ToString("N") + ".tmp");
        System.IO.File.WriteAllText(file, "x");
        try
        {
            var result = Render("${if_existing " + file + "}YES${else}NO${endif}");
            Assert.Equal("YES", result);
        }
        finally { System.IO.File.Delete(file); }
    }

    [Fact]
    public void IfExisting_ElseBranch_WhenMissing()
    {
        var missing = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dm_missing_" + Guid.NewGuid().ToString("N") + ".tmp");
        Assert.Equal("NO", Render("${if_existing " + missing + "}YES${else}NO${endif}"));
    }

    [Fact]
    public void IfMounted_SystemDrive_True()
    {
        var root = System.IO.Path.GetPathRoot(Environment.SystemDirectory)!;
        Assert.Equal("Y", Render("${if_mounted " + root + "}Y${else}N${endif}"));
    }

    [Fact]
    public void IfMatch_NumericAndString()
    {
        Assert.Equal("y", Render("${if_match 5 == 5}y${else}n${endif}"));
        Assert.Equal("y", Render("${if_match 7 > 3}y${else}n${endif}"));
        Assert.Equal("n", Render("${if_match 5 == 6}y${else}n${endif}"));
        Assert.Equal("y", Render("${if_match abc == abc}y${else}n${endif}"));
    }

    [Fact]
    public void IfRunning_CurrentProcess_True()
    {
        var name = System.Diagnostics.Process.GetCurrentProcess().ProcessName;
        Assert.Equal("Y", Render("${if_running " + name + "}Y${else}N${endif}"));
    }

    [Fact]
    public void IfEmpty_EmptyArgs_True()
    {
        Assert.Equal("yes", Render("${if_empty }yes${else}no${endif}"));
    }

    [Fact]
    public void IfUpdatenr_MatchesUpdateNumber()
    {
        Assert.Equal("y", Render("${if_updatenr 3}y${else}n${endif}", updateNumber: 3));
        Assert.Equal("n", Render("${if_updatenr 3}y${else}n${endif}", updateNumber: 5));
    }

    [Fact]
    public void IfGw_GatewayMatch()
    {
        var snap = TestHelpers.FakeSnapshot();
        var result = Render("${if_gw 192.168.1.1}Y${else}N${endif}", snap);
        Assert.Equal("N", result); // FakeSnapshot 无网关
    }

    [Fact]
    public void NestedIf_BlocksParseCorrectly()
    {
        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dm_nest_" + Guid.NewGuid().ToString("N") + ".tmp");
        System.IO.File.WriteAllText(file, "x");
        try
        {
            var result = Render("${if_existing " + file + "}A${if_match 1 == 1}B${endif}C${else}D${endif}");
            Assert.Equal("ABC", result);
        }
        finally { System.IO.File.Delete(file); }
    }

    [Fact]
    public void IfMatch_WithNestedVariableArg()
    {
        // 嵌套变量参数：${if_match ${cpu} > 10}，cpu=12 → true
        var snap = new SystemSnapshot { CpuPercent = 12 };
        Assert.Equal("y", Render("${if_match ${cpu} > 10}y${else}n${endif}", snap));
    }

    // ---- 网络信息 + loadavg ----

    private static SystemSnapshot NetSnap() => new()
    {
        InterfaceIps = new[] { "10.0.0.5" },
        GatewayIps = new[] { "10.0.0.1" },
        DnsServers = new[] { "8.8.8.8", "1.1.1.1" },
        DefaultInterfaceName = "Ethernet",
        LoadAvg1 = 0.5,
        LoadAvg5 = 0.4,
        LoadAvg15 = 0.3,
    };

    [Fact]
    public void NetworkVars_Addresses()
    {
        var snap = NetSnap();
        Assert.Equal("10.0.0.5", VariableEvaluator.Evaluate("addr", Array.Empty<string>(), snap, _settings));
        Assert.Equal("10.0.0.5", VariableEvaluator.Evaluate("addrs", Array.Empty<string>(), snap, _settings));
        Assert.Equal("10.0.0.1", VariableEvaluator.Evaluate("gw_ip", Array.Empty<string>(), snap, _settings));
        Assert.Equal("Ethernet", VariableEvaluator.Evaluate("gw_iface", Array.Empty<string>(), snap, _settings));
        Assert.Equal("Ethernet", VariableEvaluator.Evaluate("iface", Array.Empty<string>(), snap, _settings));
        Assert.Equal("8.8.8.8 1.1.1.1", VariableEvaluator.Evaluate("nameserver", Array.Empty<string>(), snap, _settings));
    }

    [Fact]
    public void Loadavg_FormatsThreeValues()
    {
        var snap = NetSnap();
        Assert.Equal("0.50 0.40 0.30", VariableEvaluator.Evaluate("loadavg", Array.Empty<string>(), snap, _settings));
    }

    [Fact]
    public void IfGw_WithGateway_True()
    {
        var snap = NetSnap();
        Assert.Equal("Y", Render("${if_gw 10.0.0.1}Y${else}N${endif}", snap));
    }

    // ---- 文本处理（第 2 批）----

    [Fact]
    public void TextOp_UppercaseLowercaseStartcase()
    {
        Assert.Equal("HELLO WORLD", Render("${uppercase hello world}"));
        Assert.Equal("hello world", Render("${lowercase HELLO WORLD}"));
        Assert.Equal("Hello World", Render("${startcase hello world}"));
    }

    [Fact]
    public void TextOp_RstripWords()
    {
        Assert.Equal("ab", Render("${rstrip ab   }"));
        Assert.Equal("3", Render("${words one two three}"));
    }

    [Fact]
    public void TextOp_ToBytes()
    {
        Assert.Equal("1024", Render("${to_bytes 1KiB}"));
        Assert.Equal("1073741824", Render("${to_bytes 1GiB}"));
        Assert.Equal("512", Render("${to_bytes 512}"));
    }

    [Fact]
    public void TextOp_CombineAndEval()
    {
        Assert.Equal("a-b-c", Render("${combine - a b c}"));
        Assert.Equal("cpu=12", Render("${eval cpu=${cpu}}")); // 嵌套变量展开
    }

    [Fact]
    public void TextOp_LinesHeadTail()
    {
        var file = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "dm_txt_" + Guid.NewGuid().ToString("N") + ".txt");
        System.IO.File.WriteAllLines(file, new[] { "a", "b", "c", "d", "e" });
        try
        {
            Assert.Equal("5", Render("${lines " + file + "}"));
            Assert.Equal("a\r\nb\r\nc", Render("${head 3 " + file + "}"));
            Assert.Equal("d\r\ne", Render("${tail 2 " + file + "}"));
        }
        finally { System.IO.File.Delete(file); }
    }

    [Fact]
    public void ExecBar_FirstPrint_PlaceholderZero()
    {
        var layout = new WidgetLayout();
        var ctx = new RenderContext(TestHelpers.FakeSnapshot(), _settings, layout);
        var nodes = ConkyTextParser.Parse("${execbar echo 42}", _registry, _settings);
        foreach (var node in nodes) node.Print(ctx);
        var bar = Assert.IsType<WidgetBar>(layout.Lines[0].Elements[0]);
        Assert.Equal(0, bar.Percent); // 首帧无输出 → 0
    }

    [Fact]
    public void Lua_Node_CallsConfigFunction()
    {
        var engine = new DeskMeter.Core.Config.LuaConfigEngine();
        var cfg = engine.Parse("function greet(name) return \"Hello, \" .. name end\nconky.config = {}\nconky.text = [[]]");
        var layout = new WidgetLayout();
        var ctx = new RenderContext(TestHelpers.FakeSnapshot(), cfg.Settings, layout) { LuaScript = cfg.LuaScript };
        var nodes = ConkyTextParser.Parse("${lua greet DeskMeter}", _registry, cfg.Settings);
        foreach (var node in nodes) node.Print(ctx);
        Assert.Equal("Hello, DeskMeter", layout.ToConsoleText().TrimEnd('\r', '\n'));
    }

    [Fact]
    public void Lua_Node_MissingFunction_Placeholder()
    {
        var engine = new DeskMeter.Core.Config.LuaConfigEngine();
        var cfg = engine.Parse("conky.config = {}\nconky.text = [[]]");
        var layout = new WidgetLayout();
        var ctx = new RenderContext(TestHelpers.FakeSnapshot(), cfg.Settings, layout) { LuaScript = cfg.LuaScript };
        var nodes = ConkyTextParser.Parse("${lua not_a_function}", _registry, cfg.Settings);
        foreach (var node in nodes) node.Print(ctx);
        Assert.Equal("--", layout.ToConsoleText().TrimEnd('\r', '\n'));
    }
}