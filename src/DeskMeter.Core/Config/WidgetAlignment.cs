namespace DeskMeter.Core.Config;

/// <summary>
/// 窗口对齐方式（对应 Conky 的 alignment 键，含 9 向及缩写）。
/// </summary>
public enum WidgetAlignment
{
    TopLeft, TopMiddle, TopRight,
    MiddleLeft, MiddleMiddle, MiddleRight,
    BottomLeft, BottomMiddle, BottomRight,
}

public static class WidgetAlignmentParser
{
    /// <summary>解析 Conky alignment 字符串（top_left / tl / top_middle / tm ...）。</summary>
    public static WidgetAlignment Parse(string? value, WidgetAlignment fallback = WidgetAlignment.TopLeft)
    {
        if (string.IsNullOrWhiteSpace(value)) return fallback;
        var v = value.Trim().ToLowerInvariant().Replace("_", "").Replace("-", "");
        return v switch
        {
            "tl" or "topleft" => WidgetAlignment.TopLeft,
            "tm" or "topmiddle" or "topcentre" => WidgetAlignment.TopMiddle,
            "tr" or "topright" => WidgetAlignment.TopRight,
            "ml" or "middleleft" => WidgetAlignment.MiddleLeft,
            "mm" or "middlemiddle" or "middlecentre" => WidgetAlignment.MiddleMiddle,
            "mr" or "middleright" => WidgetAlignment.MiddleRight,
            "bl" or "bottomleft" => WidgetAlignment.BottomLeft,
            "bm" or "bottommiddle" or "bottomcentre" => WidgetAlignment.BottomMiddle,
            "br" or "bottomright" => WidgetAlignment.BottomRight,
            _ => fallback,
        };
    }
}
