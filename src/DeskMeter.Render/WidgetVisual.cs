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

    /// <summary>最近一次重绘的测量尺寸（窗口自适应用）。</summary>
    public Size MeasuredSize => _measured;

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
        _measured = new Size(0, 0);
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

        using (var dc = _visual.RenderOpen())
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
                        new Point(_options.Padding + Math.Max(maxWidth, _options.Padding * 2), y + lineHeight / 2));
                }
                else
                {
                    var x = _options.Padding;
                    foreach (var element in line.Elements)
                    {
                        switch (element)
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
                                // Conky 语义：Width=0 → 填满本行剩余宽度
                                var w = bar.Width > 0 ? bar.Width : Math.Max(0, maxWidth - x);
                                var h = bar.Height;
                                DrawBar(dc, x, y + (lineHeight - h) / 2, w, h, bar.Brush, bar.Percent);
                                x += w;
                                break;
                            }
                        }
                    }
                }
                y += lineHeight + _options.LineGap;
            }
        }

        _measured = new Size(
            Math.Max(maxWidth, 1) + _options.Padding * 2,
            Math.Max(totalHeight, 1));
    }

    /// <summary>
    /// 矢量进度条（Conky draw_rect + fill_rect 语义）：当前色 1px 圆角描边 + 按百分比填充。
    /// </summary>
    private static void DrawBar(DrawingContext dc, double x, double y, double w, double h,
        WidgetBrush color, double percent)
    {
        if (w <= 0 || h <= 0) return;
        var fillBrush = ToBrush(color);
        var pen = new Pen(fillBrush, 1);
        var radius = Math.Min(3, h / 2);

        // 填充部分（按百分比）
        var fillW = w * Math.Clamp(percent, 0, 100) / 100.0;
        if (fillW > 0)
            dc.DrawRoundedRectangle(fillBrush, null, new Rect(x, y, fillW, h), radius, radius);

        // 描边（轨道：透明背景 + 当前色轮廓，与 Conky 一致）
        dc.DrawRoundedRectangle(null, pen, new Rect(x + 0.5, y + 0.5, w - 1, h - 1), radius, radius);
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
