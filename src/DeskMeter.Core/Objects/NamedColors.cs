using DeskMeter.Core.Config;
using System.Drawing;

namespace DeskMeter.Core.Objects;

/// <summary>
/// X11 命名颜色全量表：由 System.Drawing.KnownColor（标准 X11/CSS 扩展色，约 140 色）
/// 在运行时生成，含 grey/gray 双拼写别名。来源为标准 X11 rgb.txt 事实数据（非 Conky 源码）。
/// </summary>
public static class NamedColors
{
    private static readonly Lazy<Dictionary<string, WidgetBrush>> Map = new(Build);

    public static WidgetBrush? TryGet(string name) =>
        Map.Value.TryGetValue(name, out var b) ? b : null;

    private static Dictionary<string, WidgetBrush> Build()
    {
        var d = new Dictionary<string, WidgetBrush>(StringComparer.OrdinalIgnoreCase);
        foreach (KnownColor kc in Enum.GetValues<KnownColor>())
        {
            var c = Color.FromKnownColor(kc);
            if (c.IsSystemColor) continue; // 跳过 ActiveBorder/Control 等系统色
            d[kc.ToString()] = new WidgetBrush(c.R, c.G, c.B);
        }

        // X11 同时接受 grey/gray 两种拼写
        foreach (var pair in d.ToList())
        {
            if (pair.Key.Contains("gray", StringComparison.OrdinalIgnoreCase))
            {
                var grey = pair.Key.Replace("gray", "grey", StringComparison.OrdinalIgnoreCase);
                if (!d.ContainsKey(grey)) d[grey] = pair.Value;
            }
        }

        // 修正：.NET KnownColor.Gray=128 是 Windows 系统灰，与 X11/Conky 的 gray/grey=190 不一致
        // （Conky color-names.yml: "gray": [190,190,190]）。其余灰色族（lightgrey=211 等）已与 X11 一致。
        d["gray"] = new WidgetBrush(190, 190, 190);
        d["grey"] = new WidgetBrush(190, 190, 190);
        return d;
    }
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
