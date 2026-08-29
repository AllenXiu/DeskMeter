using System.Globalization;

namespace DeskMeter.Core.Objects;

/// <summary>RGB 颜色（渲染层与文本层共用的纯值类型）。</summary>
public readonly record struct WidgetBrush(byte R, byte G, byte B)
{
    public static WidgetBrush White => new(255, 255, 255);

    /// <summary>解析 #RRGGBB；失败返回 null。</summary>
    public static WidgetBrush? TryParseHex(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;
        var s = spec.Trim();
        if (s.StartsWith('#')) s = s[1..];
        if (s.Length != 6) return null;
        if (!byte.TryParse(s.AsSpan(0, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var r) ||
            !byte.TryParse(s.AsSpan(2, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var g) ||
            !byte.TryParse(s.AsSpan(4, 2), NumberStyles.HexNumber, CultureInfo.InvariantCulture, out var b))
            return null;
        return new WidgetBrush(r, g, b);
    }
}

/// <summary>一行中的一段文本及其颜色。</summary>
public sealed class WidgetSpan
{
    public WidgetSpan(string text, WidgetBrush brush)
    {
        Text = text;
        Brush = brush;
    }

    public string Text { get; set; }
    public WidgetBrush Brush { get; set; }
}

/// <summary>小部件的一行：普通文本行或水平分隔线。</summary>
public sealed class WidgetLine
{
    public List<WidgetSpan> Spans { get; } = new();

    /// <summary>true 表示这是 $hr 分隔线（Spans 为空）。</summary>
    public bool IsRule { get; set; }

    public WidgetBrush? RuleBrush { get; set; }

    public string PlainText => string.Concat(Spans.Select(s => s.Text));
}

/// <summary>Object Tree 的输出模型：有序行集合，供渲染后端消费（WPF / Console / File / HTTP）。</summary>
public sealed class WidgetLayout
{
    public List<WidgetLine> Lines { get; } = new();

    public WidgetLine CurrentLine => Lines.Count == 0 ? NewLine() : Lines[^1];

    public WidgetLine NewLine()
    {
        var line = new WidgetLine();
        Lines.Add(line);
        return line;
    }

    public void AppendText(string text, WidgetBrush brush)
    {
        if (string.IsNullOrEmpty(text)) return;
        var line = CurrentLine;
        if (line.IsRule) line = NewLine();
        line.Spans.Add(new WidgetSpan(text, brush));
    }

    public void AppendRule(WidgetBrush brush)
    {
        var line = NewLine();
        line.IsRule = true;
        line.RuleBrush = brush;
    }

    /// <summary>纯文本表示（Console / File 后端用；规则行画 8 个连字符）。</summary>
    public string ToConsoleText()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var line in Lines)
        {
            if (line.IsRule) sb.Append("--------");
            else sb.Append(line.PlainText);
            sb.AppendLine();
        }
        return sb.ToString();
    }
}
