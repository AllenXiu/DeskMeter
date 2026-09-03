using System.Globalization;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using DeskMeter.Core.Objects;

namespace DeskMeter.Render;

/// <summary>
/// Avalonia 小部件渲染控件：把 WidgetLayout 用 DrawingContext 矢量绘制
/// （彩色文本 run + 圆角矢量 Bar + 面积/折线 Graph；由 WPF WidgetVisual 移植）。
/// 单位：Avalonia DIP（macOS 由平台处理 Retina 缩放）。
/// </summary>
public sealed class WidgetVisualAvalonia : Control
{
    private WidgetLayout? _layout;
    private RenderOptionsMac _options;
    private Size _measured;
    private double _stableWidth;
    private readonly Dictionary<long, SolidColorBrush> _brushCache = new();
    private readonly Dictionary<(string Text, byte R, byte G, byte B, string Family, double Size), FormattedText> _ftCache = new();
    private const int FtCacheMax = 256;
    private readonly Dictionary<string, Typeface> _typefaceCache = new();
    private readonly Dictionary<(string Family, double Size), double> _spaceAdvanceCache = new();
    // 含 CJK 的行整行使用苹方：中英混排共用一套字体度量，避免数字与中文基线错位；纯 ASCII 行仍用等宽 Menlo
    private const string CjkFontFamily = "PingFang SC";
    private string _lineFontFamily = "";

    private static bool ContainsCjk(string s)
    {
        foreach (var ch in s)
            if (ch >= 0x2E80 && ch <= 0x9FFF) return true;
        return false;
    }

    private string LineFontFamily(WidgetLine line)
    {
        foreach (var e in line.Elements)
            if (e is WidgetText t && ContainsCjk(t.Text))
                return CjkFontFamily;
        return _options.FontFamily;
    }

    /// <summary>最近一次重绘的测量尺寸（窗口自适应用）。</summary>
    public Size MeasuredSize => _measured;

    /// <summary>Top 表头行区域（点击切换排序用；无表头为 null）。</summary>
    public Rect? TopHeaderBounds { get; private set; }

    public WidgetVisualAvalonia(RenderOptionsMac options)
    {
        _options = options;
    }

    public void ResetStableWidth() => _stableWidth = 0;

    public void Update(WidgetLayout layout, RenderOptionsMac? options = null)
    {
        _layout = layout;
        if (options is not null) _options = options;
        MeasureAndDraw();
        InvalidateMeasure();
        InvalidateVisual();
    }

    protected override Size MeasureOverride(Size availableSize)
    {
        return _measured.Width > 0 ? _measured : new Size(1, 1);
    }

    public override void Render(DrawingContext dc)
    {
        if (_layout is null) return;
        Draw(dc);
        base.Render(dc);
    }

    private void MeasureAndDraw()
    {
        if (_layout is null) return;
        var typeface = GetTypeface(_options.FontFamily);
        var fontHeight = TextHeight("Ag", _options.DefaultBrush, null, typeface, _options.FontSize);
        var lines = _layout.Lines;
        var lineWidths = new double[lines.Count];
        var lineHeights = new double[lines.Count];
        var maxWidth = 0.0;
        for (var i = 0; i < lines.Count; i++)
        {
            var line = lines[i];
            double w = 0, h = fontHeight;
            _lineFontFamily = LineFontFamily(line);
            if (!line.IsRule)
            {
                foreach (var element in line.Elements)
                {
                    switch (element)
                    {
                        case WidgetText text:
                        {
                            var (tf, sz) = ResolveFont(text.Font);
                            w += Measure(text.Text, text.Brush, tf, sz);
                            var th = TextHeight(text.Text, text.Brush, text.Font, tf, sz);
                            if (th > h) h = th;
                            if (sz > 0 && sz + 2 > h) h = sz + 2;
                            break;
                        }
                        case WidgetBar bar:
                            if (bar.Width > 0) w += bar.Width;
                            if (bar.Height > h) h = bar.Height;
                            break;
                        case WidgetGoto g:
                            if (g.X > w) w = g.X;
                            break;
                        case WidgetGraph graph:
                            if (graph.Width > 0) w += graph.Width;
                            if (graph.Height > h) h = graph.Height;
                            break;
                        case WidgetOffset o:
                            w += o.N;
                            break;
                        case WidgetTab t:
                            if (t.N > 0) w = (Math.Floor(w / t.N) + 1) * t.N;
                            break;
                        case WidgetVOffset v:
                            if (v.N > 0) h += v.N;
                            break;
                    }
                }
            }
            lineWidths[i] = w;
            lineHeights[i] = h;
            if (w > maxWidth) maxWidth = w;
        }
        var totalHeight = lineHeights.Sum()
            + Math.Max(0, lines.Count - 1) * _options.LineGap
            + _options.Padding * 2;
        if (maxWidth > _stableWidth) _stableWidth = maxWidth;
        var widgetWidth = WidgetMetrics.ClampWidth(_stableWidth, _options.MinimumWidth, _options.MaximumWidth);
        var widgetHeight = Math.Max(totalHeight, _options.MinimumHeight);
        _measured = new Size(Math.Max(widgetWidth, 1) + _options.Padding * 2, Math.Max(widgetHeight, 1));
        _widgetWidth = widgetWidth;
        _widgetHeight = widgetHeight;
    }

    private double _widgetWidth, _widgetHeight;

    private void Draw(DrawingContext dc)
    {
        if (_layout is null) return;
        try
        {
            TopHeaderBounds = null;
            var brush = GetBrush(_options.DefaultBrush);
            var typeface = GetTypeface(_options.FontFamily);
            var y = _options.Padding;
            for (var i = 0; i < _layout.Lines.Count; i++)
            {
                var line = _layout.Lines[i];
                _lineFontFamily = LineFontFamily(line);
                var lineHeight = MeasureLineHeight(line, typeface);
                if (line.IsRule)
                {
                    var ruleBrush = line.RuleBrush is { } rb ? GetBrush(rb) : brush;
                    var pen = new Pen(ruleBrush, 1);
                    dc.DrawLine(pen, new Point(_options.Padding, y + lineHeight / 2),
                        new Point(_options.Padding + Math.Max(_widgetWidth, _options.Padding * 2), y + lineHeight / 2));
                }
                else
                {
                    var x = _options.Padding;
                    var yShift = 0.0;
                    foreach (var element in line.Elements)
                    {
                        switch (element)
                        {
                            case WidgetText text:
                            {
                                if (text.IsTopHeader)
                                    TopHeaderBounds = new Rect(_options.Padding, y + yShift, Math.Max(_widgetWidth, 0), lineHeight);
                                var ft = GetFormattedText(text.Text, text.Brush, text.Font);
                                dc.DrawText(ft, new Point(x, y + yShift));
                                x += RunWidth(text.Text, text.Brush, text.Font);
                                break;
                            }
                            case WidgetBar bar:
                            {
                                var w = bar.Width > 0 ? bar.Width : Math.Round(Math.Max(0, _widgetWidth - x));
                                var h = bar.Height;
                                DrawBar(dc, x, y + yShift + (lineHeight - h) / 2, w, h, bar.Brush, bar.Percent);
                                x += w;
                                break;
                            }
                            case WidgetGoto g:
                                x = _options.Padding + g.X;
                                break;
                            case WidgetGraph graph:
                            {
                                var gw = graph.Width > 0 ? graph.Width : Math.Max(0, _widgetWidth - x);
                                var gh = graph.Height;
                                DrawGraph(dc, x, y + yShift + (lineHeight - gh) / 2, gw, gh, graph.Brush,
                                    graph.Series, graph.MaxOverride, graph.LogScale);
                                x += gw;
                                break;
                            }
                            case WidgetOffset o:
                                x = Math.Max(_options.Padding, x + o.N);
                                break;
                            case WidgetVOffset v:
                                yShift += v.N;
                                break;
                            case WidgetTab t:
                                if (t.N > 0)
                                    x = _options.Padding + (Math.Floor((x - _options.Padding) / t.N) + 1) * t.N;
                                break;
                            case WidgetAlignC:
                            {
                                var remaining = MeasureRemaining(line.Elements, IndexOf(line.Elements, element) + 1, typeface);
                                x = Math.Max(x, (_widgetWidth - remaining) / 2);
                                break;
                            }
                            case WidgetAlignR r:
                            {
                                var remaining = MeasureRemaining(line.Elements, IndexOf(line.Elements, element) + 1, typeface);
                                x = Math.Max(x, _widgetWidth - remaining - r.N);
                                break;
                            }
                        }
                    }
                }
                y += lineHeight + _options.LineGap;
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("Avalonia draw error: " + ex.Message);
        }
    }

    private static int IndexOf<T>(IReadOnlyList<T> list, T item)
    {
        for (var i = 0; i < list.Count; i++) if (Equals(list[i], item)) return i;
        return -1;
    }

    private double MeasureLineHeight(WidgetLine line, Typeface typeface)
    {
        double h = TextHeight("Ag", _options.DefaultBrush, null, typeface, _options.FontSize);
        foreach (var element in line.Elements)
        {
            switch (element)
            {
                case WidgetText text:
                {
                    var (tf, sz) = ResolveFont(text.Font);
                    var th = TextHeight(text.Text, text.Brush, text.Font, tf, sz);
                    if (th > h) h = th;
                    if (sz > 0 && sz + 2 > h) h = sz + 2;
                    break;
                }
                case WidgetBar bar when bar.Height > h: h = bar.Height; break;
                case WidgetGraph graph when graph.Height > h: h = graph.Height; break;
                case WidgetVOffset v: h += v.N; break;
            }
        }
        return h;
    }

    private void DrawBar(DrawingContext dc, double x, double y, double w, double h,
        WidgetBrush color, double percent)
    {
        if (w <= 0 || h <= 0) return;
        var fill = GetBrush(color);
        var pen = new Pen(fill, 1);
        var radius = Math.Min(3, h / 2);
        var fillW = w * Math.Clamp(percent, 0, 100) / 100.0;
        if (fillW >= 1)
            dc.DrawRectangle(fill, null, new RoundedRect(new Rect(x, y, fillW, h), radius, radius));
        if (w >= 2 && h >= 2)
            dc.DrawRectangle(null, pen, new RoundedRect(new Rect(x + 0.5, y + 0.5, w - 1, h - 1), radius, radius));
    }

    private void DrawGraph(DrawingContext dc, double x, double y, double w, double h,
        WidgetBrush color, IReadOnlyList<double> series, double? maxOverride = null, bool logScale = false)
    {
        if (w <= 1 || h <= 1 || series.Count == 0) return;
        double Transform(double v) => logScale ? Math.Log10(1 + Math.Max(0, v)) : v;
        var vals = series.Select(Transform).ToArray();
        var autoMax = vals.Length > 0 ? vals.Max() : 0;
        var max = Math.Max(maxOverride is > 0 ? Transform(maxOverride.Value) : autoMax, 1e-9);
        var brush = GetBrush(color);
        var pen = new Pen(brush, 1);
        var areaBrush = WithAlpha(color, 0x38);
        var pts = new Point[vals.Length];
        for (var i = 0; i < vals.Length; i++)
        {
            var px = x + (vals.Length == 1 ? 0 : i / (double)(vals.Length - 1)) * w;
            var py = y + h - 1 - Math.Clamp(vals[i] / max, 0, 1) * (h - 2);
            pts[i] = new Point(px, py);
        }
        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(pts[0], true);
            for (var i = 1; i < pts.Length; i++) g.LineTo(pts[i]);
            g.LineTo(new Point(pts[^1].X, y + h));
            g.LineTo(new Point(pts[0].X, y + h));
            g.EndFigure(true);
        }
        dc.DrawGeometry(areaBrush, null, area);
        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(pts[0], false);
            for (var i = 1; i < pts.Length; i++) g.LineTo(pts[i]);
            g.EndFigure(false);
        }
        dc.DrawGeometry(null, pen, line);
    }

    private double MeasureRemaining(IReadOnlyList<WidgetElement> elements, int from, Typeface typeface)
    {
        double w = 0;
        for (var i = from; i < elements.Count; i++)
        {
            switch (elements[i])
            {
                case WidgetText t:
                {
                    var (tf, sz) = ResolveFont(t.Font);
                    w += Measure(t.Text, t.Brush, tf, sz);
                    break;
                }
                case WidgetBar b when b.Width > 0: w += b.Width; break;
                case WidgetGraph g when g.Width > 0: w += g.Width; break;
            }
        }
        return w;
    }

    /// <summary>
    /// 含尾部空格的 run 宽度：Avalonia FormattedText.Width 与 WPF 一样剔除尾部空格，
    /// 列对齐依赖补齐空格占位，必须手动补回（空格前进步进来自内部空格实测）。
    /// </summary>
    private double Measure(string text, WidgetBrush color, Typeface typeface, double size)
    {
        return RunWidth(text, color, null, typeface, size);
    }

    private double RunWidth(string text, WidgetBrush color, FontSpec? font,
        Typeface? typeface = null, double? size = null)
    {
        var family = string.IsNullOrEmpty(font?.Family)
            ? (_lineFontFamily.Length > 0 ? _lineFontFamily : _options.FontFamily)
            : font.Value.Family;
        var fs = font is { Size: > 0 } ? font.Value.Size : size ?? _options.FontSize;
        var tf = typeface ?? GetTypeface(family);
        var ft = GetFormattedText(text, color, font, tf, fs);
        var trailing = 0;
        for (var i = text.Length - 1; i >= 0 && text[i] == ' '; i--) trailing++;
        return trailing == 0 ? ft.Width : ft.Width + trailing * SpaceAdvance(family, fs);
    }

    /// <summary>文本行高：用 FormattedText.Height（含 CJK 回退字形度量，避免跨行重叠）。</summary>
    private double TextHeight(string text, WidgetBrush color, FontSpec? font,
        Typeface? typeface = null, double? size = null)
    {
        return GetFormattedText(text, color, font, typeface, size).Height;
    }

    private double SpaceAdvance(string family, double size)
    {
        var key = (family, size);
        if (_spaceAdvanceCache.TryGetValue(key, out var cached)) return cached;
        var tf = GetTypeface(family);
        var w1 = GetFormattedText("a b", _options.DefaultBrush, null, tf, size).Width;
        var w2 = GetFormattedText("ab", _options.DefaultBrush, null, tf, size).Width;
        var adv = Math.Max(1, w1 - w2);
        _spaceAdvanceCache[key] = adv;
        return adv;
    }

    private (Typeface Typeface, double Size) ResolveFont(FontSpec? font)
    {
        var family = string.IsNullOrEmpty(font?.Family)
            ? (_lineFontFamily.Length > 0 ? _lineFontFamily : _options.FontFamily)
            : font.Value.Family;
        var size = font is { Size: > 0 } ? font.Value.Size : _options.FontSize;
        return (GetTypeface(family), size);
    }

    private Typeface GetTypeface(string family)
    {
        if (_typefaceCache.TryGetValue(family, out var cached)) return cached;
        var tf = new Typeface(new FontFamily(family));
        _typefaceCache[family] = tf;
        return tf;
    }

    private SolidColorBrush GetBrush(WidgetBrush b)
    {
        var key = ((long)b.R << 16) | ((long)b.G << 8) | b.B;
        if (_brushCache.TryGetValue(key, out var cached)) return cached;
        var brush = new SolidColorBrush(Color.FromRgb(b.R, b.G, b.B));
        _brushCache[key] = brush;
        return brush;
    }

    private SolidColorBrush WithAlpha(WidgetBrush b, byte alpha)
    {
        return new SolidColorBrush(Color.FromArgb(alpha, b.R, b.G, b.B));
    }

    private FormattedText GetFormattedText(string text, WidgetBrush color, FontSpec? font,
        Typeface? typeface = null, double? size = null)
    {
        var family = string.IsNullOrEmpty(font?.Family)
            ? (_lineFontFamily.Length > 0 ? _lineFontFamily : _options.FontFamily)
            : font.Value.Family;
        var fs = font is { Size: > 0 } ? font.Value.Size : size ?? _options.FontSize;
        var tf = typeface ?? GetTypeface(family);
        var key = (text, color.R, color.G, color.B, family, fs);
        if (_ftCache.TryGetValue(key, out var cached)) return cached;
        if (_ftCache.Count >= FtCacheMax) _ftCache.Clear();
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            tf, fs, GetBrush(color));
        _ftCache[key] = ft;
        return ft;
    }

    /// <summary>诊断：验证 FormattedText.Width 是否包含尾部空格（列对齐依赖）。</summary>
    public void DebugMeasure()
    {
        try
        {
            var tf = GetTypeface(_options.FontFamily);
            var sz = _options.FontSize;
            double W(string s) => GetFormattedText(s, _options.DefaultBrush, null, tf, sz).Width;
            Console.WriteLine("[measure] \'abc\'=" + W("abc").ToString("0.###")
                + " \'abc \'=" + W("abc ").ToString("0.###")
                + " \'abc   \'=" + W("abc   ").ToString("0.###"));
            Console.WriteLine("[measure] space-interior (\'a b\'-\'ab\')=" + (W("a b") - W("ab")).ToString("0.###")
                + " (\'x x\'-\'xx\')=" + (W("x x") - W("xx")).ToString("0.###"));
            Console.WriteLine("[measure] RunWidth \'abc   \'=" + RunWidth("abc   ", _options.DefaultBrush, null, tf, sz).ToString("0.###")
                + " vs raw=" + W("abc   ").ToString("0.###"));
            Console.WriteLine("[measure] CJK \'个进程 | \' raw=" + W("个进程 | ").ToString("0.###")
                + " RunWidth=" + RunWidth("个进程 | ", _options.DefaultBrush, null, tf, sz).ToString("0.###")
                + " | \' 个进程\' raw=" + W(" 个进程").ToString("0.###") + " RunWidth=" + RunWidth(" 个进程", _options.DefaultBrush, null, tf, sz).ToString("0.###"));
        }
        catch (Exception ex) { Console.WriteLine("[measure] err " + ex.Message); }
    }
}
