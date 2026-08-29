using DeskMeter.Core.Config;

namespace DeskMeter.Core.Objects;

/// <summary>X11 命名颜色表（子集；P1 补全，来源为标准 X11 rgb.txt，非 Conky 源码）。</summary>
public static class NamedColors
{
    private static readonly Dictionary<string, WidgetBrush> Map = Build();

    private static Dictionary<string, WidgetBrush> Build()
    {
        var d = new Dictionary<string, WidgetBrush>(StringComparer.OrdinalIgnoreCase);
        void Add(string name, string hex) => d[name] = WidgetBrush.TryParseHex(hex)!.Value;
        Add("white", "FFFFFF"); Add("black", "000000");
        Add("grey", "808080"); Add("gray", "808080");
        Add("lightgrey", "D3D3D3"); Add("lightgray", "D3D3D3");
        Add("darkgrey", "A9A9A9"); Add("darkgray", "A9A9A9");
        Add("red", "FF0000"); Add("darkred", "8B0000"); Add("lightcoral", "F08080");
        Add("green", "008000"); Add("darkgreen", "006400"); Add("lime", "00FF00"); Add("limegreen", "32CD32");
        Add("blue", "0000FF"); Add("darkblue", "00008B"); Add("lightblue", "ADD8E6"); Add("steelblue", "4682B4");
        Add("yellow", "FFFF00"); Add("gold", "FFD700"); Add("khaki", "F0E68C");
        Add("cyan", "00FFFF"); Add("aqua", "00FFFF"); Add("turquoise", "40E0D0");
        Add("magenta", "FF00FF"); Add("fuchsia", "FF00FF"); Add("purple", "800080"); Add("violet", "EE82EE");
        Add("orange", "FFA500"); Add("darkorange", "FF8C00");
        Add("brown", "A52A2A"); Add("pink", "FFC0CB"); Add("hotpink", "FF69B4");
        Add("silver", "C0C0C0"); Add("navy", "000080"); Add("teal", "008080"); Add("olive", "808000");
        Add("maroon", "800000"); Add("coral", "FF7F50"); Add("salmon", "FA8072");
        Add("indigo", "4B0082"); Add("orchid", "DA70D6"); Add("plum", "DDA0DD");
        return d;
    }

    public static WidgetBrush? TryGet(string name) => Map.TryGetValue(name, out var b) ? b : null;
}

/// <summary>颜色规格解析：#RRGGBB / 0-9 调色板 / X11 命名颜色 / 空=默认。</summary>
public static class ColorParser
{
    public static WidgetBrush Parse(string? spec, ConfigSettings? settings = null)
    {
        if (string.IsNullOrWhiteSpace(spec))
            return WidgetBrush.TryParseHex(settings?.GetDefaultColor()) ?? WidgetBrush.White;

        var s = spec.Trim();
        if (s.StartsWith('#'))
            return WidgetBrush.TryParseHex(s) ?? WidgetBrush.White;

        if (s.Length == 1 && s[0] >= '0' && s[0] <= '9')
        {
            var hex = settings?.GetString("color" + s[0]);
            return WidgetBrush.TryParseHex(hex) ?? WidgetBrush.White;
        }

        return NamedColors.TryGet(s) ?? WidgetBrush.White;
    }
}
