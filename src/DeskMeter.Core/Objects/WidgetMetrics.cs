namespace DeskMeter.Core.Objects;

/// <summary>
/// 窗口尺寸计算（≈ Conky update_text_area 的 text_size 语义）：
/// width = max(minimum_width, min(内容宽, maximum_width))；maximum_width 为 0 表示不设上限。
/// </summary>
public static class WidgetMetrics
{
    public static double ClampWidth(double contentWidth, double minimumWidth, double maximumWidth)
    {
        var capped = maximumWidth > 0 ? Math.Min(contentWidth, maximumWidth) : contentWidth;
        return Math.Max(capped, minimumWidth);
    }
}
