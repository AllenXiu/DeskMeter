using DeskMeter.Core.Config;
using DeskMeter.Core.Objects;

namespace DeskMeter.Render;

/// <summary>渲染选项：字体/字号/行距/内边距（来自 conky.config 的 font 等键）。</summary>
public sealed record RenderOptions
{
    public string FontFamily { get; init; } = "Consolas";
    public double FontSize { get; init; } = 12;
    public double LineGap { get; init; } = 4;
    public double Padding { get; init; } = 4;
    public WidgetBrush DefaultBrush { get; init; } = WidgetBrush.White;

    /// <summary>解析 Conky 字体串（"Consolas:size=12" / "DejaVu Sans Mono:size=12"）。</summary>
    public static RenderOptions FromSettings(ConfigSettings settings)
    {
        var options = new RenderOptions();
        var font = settings.GetString("font");
        if (!string.IsNullOrWhiteSpace(font))
        {
            var parts = font.Split(':', StringSplitOptions.RemoveEmptyEntries);
            options = options with { FontFamily = parts[0].Trim() };
            foreach (var p in parts.Skip(1))
            {
                var kv = p.Trim().Split('=', StringSplitOptions.RemoveEmptyEntries);
                if (kv.Length == 2 && kv[0].Trim().Equals("size", StringComparison.OrdinalIgnoreCase)
                    && double.TryParse(kv[1].Trim(), System.Globalization.NumberStyles.Any,
                        System.Globalization.CultureInfo.InvariantCulture, out var size) && size > 0)
                {
                    options = options with { FontSize = size };
                }
            }
        }
        return options with { DefaultBrush = ColorParser.Parse(settings.GetDefaultColor(), settings) };
    }
}
