using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DeskMeter.Core.Objects;

namespace DeskMeter.Render;

/// <summary>
/// 小部件渲染控件：把 Object Tree 输出的 WidgetLayout 用 DrawingVisual 矢量绘制
/// （等宽文本 + 分隔线 + 矢量 Bar；Graph 曲线图 P1 加入）。
/// </summary>
public sealed class WidgetVisual : FrameworkElement
{
    private readonly DrawingVisual _visual = new();
    private WidgetLayout? _layout;
    private RenderOptions _options;
    private Size _measured;
    private double _stableWidth; // 宽度只增不减（防内容变化导致窗口抖动）

    /// <summary>最近一次重绘的测量尺寸（窗口自适应用）。</summary>
    public Size MeasuredSize => _measured;

    /// <summary>配置重载时重置稳定宽度基线。</summary>
    public void ResetStableWidth() => _stableWidth = 0;

    public WidgetVisual(RenderOptions options)
    {
        _options = options;
        AddVisualChild(_visual);
    }

    /// <summary>更新布局并重绘（UI 线程调用）。</summary>
    public void Update(WidgetLayout layout, RenderOptions? options = null)
    {
        _layout = layout;
        if (options is not null) _options = options;
        Redraw();
        InvalidateMeasure();
    }

    protected override int VisualChildrenCount => 1;

    protected override Visual GetVisualChild(int index) => _visual;

    protected override Size MeasureOverride(Size availableSize) => _measured;

    private void Redraw()
    {
        if (_layout is null) return;

        var dpi = VisualTreeHelper.GetDpi(this).PixelsPerDip;
        var typeface = new Typeface(new FontFamily(_options.FontFamily), FontStyles.Normal,
            FontWeights.Normal, FontStretches.Normal);
        var brush = ToBrush(_options.DefaultBrush);

        var fontHeight = Measure("Ag", typeface, _options.FontSize, dpi);

        // 第一遍：测量每行宽度与行高（bar 显式宽度计入；Width=0 的行按剩余宽度绘制）
        var lineWidths = new double[_layout.Lines.Count];
        var lineHeights = new double[_layout.Lines.Count];
        var maxWidth = 0.0;
        for (var i = 0; i < _layout.Lines.Count; i++)
        {
            var line = _layout.Lines[i];
            double w = 0;
            double h = fontHeight;
            if (!line.IsRule)
            {
                foreach (var element in line.Elements)
                {
                    switch (element)
                    {
                        case WidgetText text:
                            w += Measure(text.Text, typeface, _options.FontSize, dpi);
                            break;
                        case WidgetBar bar:
                            if (bar.Width > 0) w += bar.Width;
                            if (bar.Height > h) h = bar.Height;
                            break;
                        case WidgetGoto g:
                            // Conky：goto 位置计入行宽
                            if (g.X > w) w = g.X;
                            break;
                        case WidgetGraph graph:
                            if (graph.Width > 0) w += graph.Width;
                            if (graph.Height > h) h = graph.Height;
                            break;
                    }
                }
            }
            lineWidths[i] = w;
            lineHeights[i] = h;
            if (w > maxWidth) maxWidth = w;
        }

        var totalHeight = lineHeights.Sum()
            + Math.Max(0, _layout.Lines.Count - 1) * _options.LineGap
            + _options.Padding * 2;

        // 稳定宽度：只增不减（DeskMeter 增强，防官方配置等无 min/max 时窗口抖动），
        // 再按 Conky text_size 语义钳制：max(minimum_width, min(稳定宽, maximum_width))
        if (maxWidth > _stableWidth) _stableWidth = maxWidth;
        var widgetWidth = WidgetMetrics.ClampWidth(_stableWidth, _options.MinimumWidth, _options.MaximumWidth);
        var widgetHeight = Math.Max(totalHeight, _options.MinimumHeight);

        // 异常安全：绘制失败时 Abort 丢弃部分内容，保留上一次完整渲染与尺寸
        var dc = _visual.RenderOpen();
        try
        {
            var y = _options.Padding;
            for (var i = 0; i < _layout.Lines.Count; i++)
            {
                var line = _layout.Lines[i];
                var lineHeight = lineHeights[i];
                if (line.IsRule)
                {
                    var ruleBrush = line.RuleBrush is { } rb ? ToBrush(rb) : brush;
                    var pen = new Pen(ruleBrush, 1);
                    dc.DrawLine(pen, new Point(_options.Padding, y + lineHeight / 2),
                        new Point(_options.Padding + Math.Max(widgetWidth, _options.Padding * 2), y + lineHeight / 2));
                }
                else
                {
                    var x = _options.Padding;
                    for (var ei = 0; ei < line.Elements.Count; ei++)
                    {
                        switch (line.Elements[ei])
                        {
                            case WidgetText text:
                            {
                                var ft = new FormattedText(text.Text, CultureInfo.InvariantCulture,
                                    FlowDirection.LeftToRight, typeface, _options.FontSize, ToBrush(text.Brush), dpi);
                                dc.DrawText(ft, new Point(x, y));
                                x += ft.Width;
                                break;
                            }
                            case WidgetBar bar:
                            {
                                // Conky 语义：Width=0 → 填满本行剩余宽度（取整防亚像素矩形）
                                var w = bar.Width > 0 ? bar.Width : Math.Round(Math.Max(0, widgetWidth - x));
                                var h = bar.Height;
                                DrawBar(dc, x, y + (lineHeight - h) / 2, w, h, bar.Brush, bar.Percent);
                                x += w;
                                break;
                            }
                            case WidgetGoto g:
                            {
                                // Conky：cur_x = arg（相对文本区起点，绝对定位）
                                x = _options.Padding + g.X;
                                break;
                            }
                            case WidgetGraph graph:
                            {
                                var gw = graph.Width > 0 ? graph.Width : Math.Max(0, widgetWidth - x);
                                var gh = graph.Height;
                                DrawGraph(dc, x, y + (lineHeight - gh) / 2, gw, gh, graph.Brush, graph.Series);
                                x += gw;
                                break;
                            }
                            case WidgetAlignC:
                            {
                                // Conky ALIGNC：剩余内容在窗口内容宽度内居中
                                var remaining = MeasureRemaining(line.Elements, ei + 1, typeface, dpi);
                                x = Math.Max(x, (widgetWidth - remaining) / 2);
                                break;
                            }
                            case WidgetAlignR r:
                            {
                                // Conky ALIGNR：剩余内容右对齐，右缘留 N 像素
                                var remaining = MeasureRemaining(line.Elements, ei + 1, typeface, dpi);
                                x = Math.Max(x, widgetWidth - remaining - r.N);
                                break;
                            }
                        }
                    }
                }
                y += lineHeight + _options.LineGap;
            }
            dc.Close();
        }
        catch (Exception ex)
        {
            // 尺寸守卫已覆盖已知抛点（负尺寸矩形）；此处兜底：不更新 _measured，下次刷新自愈
            System.Diagnostics.Debug.WriteLine("DeskMeter draw error: " + ex.Message);
            return;
        }

        _measured = new Size(
            Math.Max(widgetWidth, 1) + _options.Padding * 2,
            Math.Max(widgetHeight, 1));
    }

    /// <summary>
    /// 矢量进度条（Conky draw_rect + fill_rect 语义）：当前色 1px 圆角描边 + 按百分比填充。
    /// </summary>
    private static void DrawBar(DrawingContext dc, double x, double y, double w, double h,
        WidgetBrush color, double percent)
    {
        // 尺寸守卫：任何非正尺寸都会让 DrawRoundedRectangle 抛异常（曾导致绘制中断/内容残缺）
        if (w <= 0 || h <= 0) return;
        var fillBrush = ToBrush(color);
        var pen = new Pen(fillBrush, 1);
        var radius = Math.Min(3, h / 2);

        // 填充部分（按百分比；亚像素宽度不画）
        var fillW = w * Math.Clamp(percent, 0, 100) / 100.0;
        if (fillW >= 1)
            dc.DrawRoundedRectangle(fillBrush, null, new Rect(x, y, fillW, h), radius, radius);

        // 描边（轨道：透明背景 + 当前色轮廓；w/h 不足 2px 时跳过，避免负尺寸矩形）
        if (w >= 2 && h >= 2)
            dc.DrawRoundedRectangle(null, pen, new Rect(x + 0.5, y + 0.5, w - 1, h - 1), radius, radius);
    }

    /// <summary>
    /// 矢量曲线图（设计稿：折线 + 面积渐变填充）：按系列最大值自动缩放，折线为当前色。
    /// </summary>
    private static void DrawGraph(DrawingContext dc, double x, double y, double w, double h,
        WidgetBrush color, IReadOnlyList<double> series)
    {
        if (w <= 1 || h <= 1 || series.Count == 0) return;
        var max = Math.Max(series.Max(), 1e-9);
        var pen = new Pen(ToBrush(color), 1);
        var areaBrush = ToBrushWithAlpha(color, 0x38);

        var points = new Point[series.Count];
        for (var i = 0; i < series.Count; i++)
        {
            var px = x + (series.Count == 1 ? 0 : i / (double)(series.Count - 1)) * w;
            var py = y + h - 1 - Math.Clamp(series[i] / max, 0, 1) * (h - 2);
            points[i] = new Point(px, py);
        }

        // 面积填充（底边闭合）
        var area = new StreamGeometry();
        using (var g = area.Open())
        {
            g.BeginFigure(points[0], true, true);
            for (var i = 1; i < points.Length; i++) g.LineTo(points[i], true, false);
            g.LineTo(new Point(points[^1].X, y + h), true, false);
            g.LineTo(new Point(points[0].X, y + h), true, false);
        }
        area.Freeze();
        dc.DrawGeometry(areaBrush, null, area);

        // 折线
        var line = new StreamGeometry();
        using (var g = line.Open())
        {
            g.BeginFigure(points[0], false, false);
            for (var i = 1; i < points.Length; i++) g.LineTo(points[i], true, false);
        }
        line.Freeze();
        dc.DrawGeometry(null, pen, line);
    }

    private static SolidColorBrush ToBrushWithAlpha(WidgetBrush b, byte alpha)
    {
        var brush = new SolidColorBrush(Color.FromArgb(alpha, b.R, b.G, b.B));
        brush.Freeze();
        return brush;
    }

    /// <summary>测量从 from 开始的本行剩余元素宽度（text 与显式宽度的 bar/graph；align/goto 不计）。</summary>
    private double MeasureRemaining(System.Collections.Generic.IReadOnlyList<WidgetElement> elements,
        int from, Typeface typeface, double dpi)
    {
        double w = 0;
        for (var i = from; i < elements.Count; i++)
        {
            switch (elements[i])
            {
                case WidgetText t:
                    w += Measure(t.Text, typeface, _options.FontSize, dpi);
                    break;
                case WidgetBar b when b.Width > 0:
                    w += b.Width;
                    break;
                case WidgetGraph g when g.Width > 0:
                    w += g.Width;
                    break;
            }
        }
        return w;
    }

    private static double Measure(string text, Typeface typeface, double size, double dpi)
    {
        var ft = new FormattedText(text, CultureInfo.InvariantCulture, FlowDirection.LeftToRight,
            typeface, size, Brushes.Black, dpi);
        return ft.Width;
    }

    private static SolidColorBrush ToBrush(WidgetBrush b)
    {
        var brush = new SolidColorBrush(Color.FromRgb(b.R, b.G, b.B));
        brush.Freeze();
        return brush;
    }
}
