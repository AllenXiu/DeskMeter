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

/// <summary>行内字体规格（Conky ${font 家族:size=字号}）。null/默认由渲染层使用配置字体。</summary>
public readonly record struct FontSpec(string Family, double Size)
{
    /// <summary>解析 "Family" 或 "Family:size=12"（忽略 bold/italic 等其余段）。</summary>
    public static FontSpec? Parse(string? spec)
    {
        if (string.IsNullOrWhiteSpace(spec)) return null;
        var parts = spec.Split(':', StringSplitOptions.RemoveEmptyEntries);
        var family = parts[0].Trim();
        if (family.Length == 0) return null;
        double size = 0;
        foreach (var p in parts.Skip(1))
        {
            var kv = p.Trim().Split('=', StringSplitOptions.RemoveEmptyEntries);
            if (kv.Length == 2 && kv[0].Trim().Equals("size", StringComparison.OrdinalIgnoreCase) &&
                double.TryParse(kv[1].Trim(), NumberStyles.Any, CultureInfo.InvariantCulture, out var s) && s > 0)
                size = s;
        }
        return new FontSpec(family, size);
    }
}

/// <summary>一行中的元素基类（文本或矢量 Bar）。</summary>
public abstract class WidgetElement
{
}

/// <summary>文本元素（可携带行内字体，null = 使用配置字体）。</summary>
public sealed class WidgetText : WidgetElement
{
    public WidgetText(string text, WidgetBrush brush, FontSpec? font = null)
    {
        Text = text;
        Brush = brush;
        Font = font;
    }

    public string Text { get; }
    public WidgetBrush Brush { get; }

    /// <summary>行内字体（${font ...} 设置）；null = 配置默认字体。</summary>
    public FontSpec? Font { get; }
}

/// <summary>
/// 矢量进度条元素（Conky bar 语义）：
/// 高度/宽度为像素；Width=0 表示填满本行剩余宽度（Conky GUI 行为）。
/// </summary>
public sealed class WidgetBar : WidgetElement
{
    public WidgetBar(double percent, WidgetBrush brush, double height, double width)
    {
        Percent = percent;
        Brush = brush;
        Height = height;
        Width = width;
    }

    /// <summary>0-100。</summary>
    public double Percent { get; }

    public WidgetBrush Brush { get; }

    /// <summary>像素高度。</summary>
    public double Height { get; }

    /// <summary>像素宽度；0 = 填满本行剩余宽度。</summary>
    public double Width { get; }
}

/// <summary>
/// 矢量曲线图元素（Conky graph 语义）：携带最近 N 个采样点序列；
/// 高度/宽度为像素，Width=0 表示填满本行剩余宽度。
/// </summary>
public sealed class WidgetGraph : WidgetElement
{
    public WidgetGraph(IReadOnlyList<double> series, WidgetBrush brush, double height, double width,
        double? maxOverride = null, bool logScale = false)
    {
        Series = series;
        Brush = brush;
        Height = height;
        Width = width;
        MaxOverride = maxOverride;
        LogScale = logScale;
    }

    /// <summary>采样序列（旧→新，FR-VIZ-2 默认最近 80 点）。</summary>
    public IReadOnlyList<double> Series { get; }

    public WidgetBrush Brush { get; }

    /// <summary>像素高度。</summary>
    public double Height { get; }

    /// <summary>像素宽度；0 = 填满本行剩余宽度。</summary>
    public double Width { get; }

    /// <summary>-m 旗标：固定最大值（0/空 = 按系列自动缩放）。</summary>
    public double? MaxOverride { get; }

    /// <summary>-l 旗标：对数刻度（log1p 变换）。</summary>
    public bool LogScale { get; }
}

/// <summary>${alignc}：把本行剩余内容水平居中（相对窗口内容宽度，Conky ALIGNC 语义）。</summary>
public sealed class WidgetAlignC : WidgetElement
{
}

/// <summary>${alignr N}：把本行剩余内容右对齐（右缘留 N 像素，Conky ALIGNR 语义）。</summary>
public sealed class WidgetAlignR : WidgetElement
{
    public WidgetAlignR(double n)
    {
        N = n;
    }

    /// <summary>距右缘的像素空隙。</summary>
    public double N { get; }
}

/// <summary>${goto N}：把当前绘制位置跳到本行第 N 像素列（相对文本区起点，Conky GOTO 语义）。</summary>
public sealed class WidgetGoto : WidgetElement
{
    public WidgetGoto(double x)
    {
        X = x;
    }

    /// <summary>像素列（相对文本区左缘）。</summary>
    public double X { get; }
}

/// <summary>${offset N}：当前绘制位置向右偏移 N 像素（可为负 = 向左）。</summary>
public sealed class WidgetOffset : WidgetElement
{
    public WidgetOffset(double n)
    {
        N = n;
    }

    public double N { get; }
}

/// <summary>${voffset N}：本行后续内容垂直偏移 N 像素（为正向下）。</summary>
public sealed class WidgetVOffset : WidgetElement
{
    public WidgetVOffset(double n)
    {
        N = n;
    }

    public double N { get; }
}

/// <summary>${tab N}：制表位——前进到下一个 N 像素整倍列（相对文本区左缘）。</summary>
public sealed class WidgetTab : WidgetElement
{
    public WidgetTab(double n)
    {
        N = n;
    }

    public double N { get; }
}

/// <summary>小部件的一行：普通文本行或水平分隔线。</summary>
public sealed class WidgetLine
{
    public List<WidgetElement> Elements { get; } = new();

    /// <summary>true 表示这是 $hr 分隔线（Elements 为空）。</summary>
    public bool IsRule { get; set; }

    public WidgetBrush? RuleBrush { get; set; }

    /// <summary>纯文本拼接（不含 bar），供制表对齐与文本断言使用。</summary>
    public string PlainText => string.Concat(Elements.OfType<WidgetText>().Select(e => e.Text));
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

    public void AppendText(string text, WidgetBrush brush, FontSpec? font = null)
    {
        if (string.IsNullOrEmpty(text)) return;
        var line = CurrentLine;
        if (line.IsRule) line = NewLine();
        line.Elements.Add(new WidgetText(text, brush, font));
    }

    public void AppendBar(double percent, WidgetBrush brush, double height, double width)
    {
        var line = CurrentLine;
        if (line.IsRule) line = NewLine();
        line.Elements.Add(new WidgetBar(percent, brush, height, width));
    }

    public void AppendGoto(double x)
    {
        var line = CurrentLine;
        if (line.IsRule) line = NewLine();
        line.Elements.Add(new WidgetGoto(x));
    }

    public void AppendGraph(IReadOnlyList<double> series, WidgetBrush brush, double height, double width,
        double? maxOverride = null, bool logScale = false)
    {
        var line = CurrentLine;
        if (line.IsRule) line = NewLine();
        line.Elements.Add(new WidgetGraph(series, brush, height, width, maxOverride, logScale));
    }

    public void AppendOffset(double n)
    {
        var line = CurrentLine;
        if (line.IsRule) line = NewLine();
        line.Elements.Add(new WidgetOffset(n));
    }

    public void AppendVOffset(double n)
    {
        var line = CurrentLine;
        if (line.IsRule) line = NewLine();
        line.Elements.Add(new WidgetVOffset(n));
    }

    public void AppendTab(double n)
    {
        var line = CurrentLine;
        if (line.IsRule) line = NewLine();
        line.Elements.Add(new WidgetTab(n));
    }

    public void AppendAlignC()
    {
        var line = CurrentLine;
        if (line.IsRule) line = NewLine();
        line.Elements.Add(new WidgetAlignC());
    }

    public void AppendAlignR(double n)
    {
        var line = CurrentLine;
        if (line.IsRule) line = NewLine();
        line.Elements.Add(new WidgetAlignR(n));
    }

    public void AppendRule(WidgetBrush brush)
    {
        // $hr：若当前行是空行（如上一段文字换行后），直接把该行变成规则行，避免 hr 上方空出一块
        var line = CurrentLine;
        if (line.Elements.Count == 0 && !line.IsRule)
        {
            line.IsRule = true;
            line.RuleBrush = brush;
            return;
        }
        line = NewLine();
        line.IsRule = true;
        line.RuleBrush = brush;
    }

    /// <summary>
    /// 纯文本表示（Console / File 后端用）。
    /// 规则行画 8 个连字符；bar 按 Conky console 风格输出（console_bar_fill="#" console_bar_unfill="."，
    /// 宽度=像素宽，0 时用 DEFAULT_BAR_WIDTH_NO_X=10）。
    /// </summary>
    public string ToConsoleText()
    {
        var sb = new System.Text.StringBuilder();
        foreach (var line in Lines)
        {
            if (line.IsRule)
            {
                sb.Append("--------");
            }
            else
            {
                foreach (var element in line.Elements)
                {
                    switch (element)
                    {
                        case WidgetText text:
                            sb.Append(text.Text);
                            break;
                        case WidgetBar bar:
                            var chars = bar.Width > 0 ? (int)Math.Round(bar.Width) : 10;
                            var fill = (int)Math.Round(Math.Clamp(bar.Percent, 0, 100) / 100.0 * chars);
                            sb.Append(new string('#', fill));
                            sb.Append(new string('.', chars - fill));
                            break;
                        case WidgetGoto:
                        case WidgetOffset:
                        case WidgetVOffset:
                        case WidgetTab:
                            // Conky console 后端忽略像素定位元素（仅 GUI 生效）
                            break;
                        case WidgetGraph graph:
                            sb.Append(GraphToTicks(graph));
                            break;
                        case WidgetAlignC:
                        case WidgetAlignR:
                            // Conky console 后端忽略对齐对象（仅 GUI 生效）
                            break;
                    }
                }
            }
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>
    /// 曲线图 console 回退：Conky console_graph_ticks（" ,_,=,#"，5 档），
    /// 宽度 0 时用 40 列（GUI 才填满剩余宽度）。
    /// </summary>
    private static string GraphToTicks(WidgetGraph graph)
    {
        if (graph.Series.Count == 0) return string.Empty;
        var ticks = new[] { ' ', ',', '_', '=', '#' };
        var vals = graph.Series.Select(Transform).ToArray();
        var max = Math.Max(graph.MaxOverride ?? 0, vals.Max());
        var cols = graph.Width > 0
            ? Math.Clamp((int)Math.Round(graph.Width / 12.0), 4, 80)
            : 40;
        var start = Math.Max(0, vals.Length - cols);
        var sb = new System.Text.StringBuilder();
        for (var i = start; i < vals.Length; i++)
        {
            var norm = max > 0 ? vals[i] / max : 0;
            var idx = Math.Clamp((int)(norm * (ticks.Length - 1)), 0, ticks.Length - 1);
            sb.Append(ticks[idx]);
        }
        return sb.ToString();

        double Transform(double v) => graph.LogScale ? Math.Log10(1 + v) : v;
    }
}
