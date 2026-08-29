using DeskMeter.Core.Config;
using DeskMeter.Core.Objects;

namespace DeskMeter.Render;

/// <summary>渲染选项：字体/字号/行距/内边距/尺寸约束（来自 conky.config）。</summary>
public sealed record RenderOptions
{
    public string FontFamily { get; init; } = "Consolas";
    public double FontSize { get; init; } = 12;
    public double LineGap { get; init; } = 4;
    public double Padding { get; init; } = 4;
    public WidgetBrush DefaultBrush { get; init; } = WidgetBrush.White;

    /// <summary>最小窗口宽度（minimum_width / minimum_size，Conky text_size 下限）。</summary>
    public double MinimumWidth { get; init; }

    /// <summary>最小窗口高度（minimum_height / minimum_size）。</summary>
    public double MinimumHeight { get; init; }

    /// <summary>最大窗口宽度（maximum_width，Conky text_size 上限；0 = 不限）。</summary>
    public double MaximumWidth { get; init; }

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
}
