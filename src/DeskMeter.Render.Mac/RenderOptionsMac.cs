using DeskMeter.Core.Config;
using DeskMeter.Core.Objects;

namespace DeskMeter.Render;

/// <summary>macOS 渲染选项（Conky 字体串 → Avalonia 字体；Windows 字体映射到 Menlo 等宽）。</summary>
public sealed record RenderOptionsMac
{
    public string FontFamily { get; init; } = "Menlo";
    public double FontSize { get; init; } = 12;
    public double LineGap { get; init; } = 4;
    public double Padding { get; init; } = 4;
    public WidgetBrush DefaultBrush { get; init; } = WidgetBrush.White;
    public double MinimumWidth { get; init; }
    public double MinimumHeight { get; init; }
    public double MaximumWidth { get; init; }

    public static RenderOptionsMac FromSettings(ConfigSettings settings)
    {
        var options = new RenderOptionsMac();
        var font = settings.GetString("font");
        if (!string.IsNullOrWhiteSpace(font))
        {
            var parts = font.Split(':', StringSplitOptions.RemoveEmptyEntries);
            options = options with { FontFamily = MapFamily(parts[0].Trim()) };
            foreach (var p in parts.Skip(1))
            {
                var kv = p.Trim().Split('=', StringSplitOptions.RemoveEmptyEntries);
                if (kv.Length == 2 && kv[0].Trim().Equals("size", StringComparison.OrdinalIgnoreCase) &&
                    double.TryParse(kv[1].Trim(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var size) && size > 0)
                    options = options with { FontSize = size };
            }
        }
        var minSize = settings.GetMinimumSize();
        var minWidth = settings.GetNumber("minimum_width", minSize.Width);
        var minHeight = settings.GetNumber("minimum_height", minSize.Height);
        var maxWidth = settings.GetNumber("maximum_width", 0);
        return options with
        {
            DefaultBrush = ColorParser.Parse(settings.GetDefaultColor(), settings),
            MinimumWidth = minWidth > 0 ? minWidth : 0,
            MinimumHeight = minHeight > 0 ? minHeight : 0,
            MaximumWidth = maxWidth > 0 ? maxWidth : 0,
        };
    }

    private static readonly HashSet<string> WindowsOnlyMonos = new(StringComparer.OrdinalIgnoreCase)
    {
        "Consolas", "Cascadia Mono", "Courier New", "Lucida Console", "DejaVu Sans Mono",
    };

    private static string MapFamily(string family)
    {
        return WindowsOnlyMonos.Contains(family) ? "Menlo" : family;
    }
}
