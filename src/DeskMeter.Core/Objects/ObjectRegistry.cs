using DeskMeter.Core.Config;

namespace DeskMeter.Core.Objects;

/// <summary>
/// 对象注册表（≈ Conky OBJ / OBJ_ARG 宏注册表）：变量名（大小写不敏感）→ 节点工厂。
/// </summary>
public sealed class ObjectRegistry
{
    private readonly Dictionary<string, Func<string[], ConfigSettings, ObjectNode>> _factories =
        new(StringComparer.OrdinalIgnoreCase);

    public ObjectRegistry()
    {
        // 文本与布局
        Register("hr", (_, _) => new RuleNode());
        Register("newline", (_, _) => new NewlineNode());
        Register("scroll", (args, settings) => new ScrollNode(args, this, settings));
        Register("font", (_, _) => new FontNode());
        Register("alignc", (_, _) => new LayoutNode("alignc", 0));
        Register("alignr", (args, _) => new LayoutNode("alignr", ParseInt(Arg(args, 0), 0)));
        Register("goto", (args, _) => new LayoutNode("goto", ParseInt(Arg(args, 0), 0)));
        Register("offset", (args, _) => new LayoutNode("offset", ParseInt(Arg(args, 0), 0)));
        Register("voffset", (args, _) => new LayoutNode("voffset", ParseInt(Arg(args, 0), 0)));
        Register("tab", (args, _) => new LayoutNode("tab", ParseInt(Arg(args, 0), 0)));

        // 颜色（特殊：需要 settings）
        Register("color", (args, settings) => new ColorNode(args.Length > 0 ? args[0] : null, settings));
        for (var i = 0; i <= 9; i++)
        {
            // Conky 也支持 $color0..$color9 独立变量形式（调色板第 N 色）
            var idx = i.ToString();
            Register("color" + idx, (_, settings) => new ColorNode(idx, settings));
        }

        // 进度条（矢量 Bar，Conky 语义：高度[,宽度]）
        Register("cpubar", (args, s) => new BarNode(d => d.CpuPercent, args, s));
        Register("membar", (args, s) => new BarNode(d => d.MemPercent, args, s));
        Register("swapbar", (args, s) => new BarNode(d => d.SwapPercent, args, s));
        Register("fs_bar", (args, s) =>
        {
            // Conky 语法：\${fs_bar [高度[,宽]] 路径}——路径是最后一个参数
            var path = args.Length > 0 && LooksLikePath(args[^1]) ? args[^1] : "/";
            var sizeArgs = args.Length > 0 && LooksLikePath(args[^1]) ? args[..^1] : args;
            return new BarNode(d => 100 - d.GetDisk(path).FreePercent, sizeArgs, s);
        });

        // 一般变量（可变参数）
        string[] plain = ["hostname", "nodename", "sysname", "kernel", "machine", "conky_version",
            "uptime", "time", "date", "cpu", "cpugraph", "mem", "memmax", "memperc",
            "swap", "swapmax", "swapperc", "fs_used", "fs_free", "fs_size", "fs_free_perc",
            "fs_used_perc", "fs_type", "downspeed", "upspeed", "downspeedf", "upspeedf",
            "totaldown", "totalup", "processes", "running_processes", "freq", "freq_g",
            "top", "top_mem", "exec", "execpi",
            "acpi", "acpitemp", "apm_adapter", "apm_battery", "apcupsd", "battery", "battery_time",
            "battery_percent", "battery_short", "hddtemp", "platform", "i2c", "smapi",
            "mpd_artist", "mpd_title", "mpd_album", "mpd_vol", "mpd_random", "mpc",
            "xmms2_artist", "audacious_title", "imap_unseen", "imap_messages", "pop3_unseen",
            "pop3_messages", "rss", "weather", "curl", "stock", "image", "nvidia", "xkb"];
        foreach (var name in plain)
            Register(name, (args, _) => new VariableNode(name, args));
    }

    private static bool LooksLikePath(string s) =>
        s.StartsWith('/') || s.StartsWith('\\') || (s.Length >= 2 && s[1] == ':');

    public ObjectNode Create(string name, string[] args, ConfigSettings settings)
    {
        if (_factories.TryGetValue(name, out var factory)) return factory(args, settings);
        return new UnknownVariableNode(name);
    }

    private void Register(string name, Func<string[], ConfigSettings, ObjectNode> factory) =>
        _factories[name] = factory;

    private static string Arg(string[] args, int i) => i < args.Length ? args[i] : string.Empty;

    private static int ParseInt(string s, int fallback)
        => int.TryParse(s, out var v) ? v : fallback;
}
