using System.Globalization;
using System.Windows;
using System.Windows.Media;
using DeskMeter.Core.Objects;

namespace DeskMeter.Render;

/// <summary>
/// 小部件渲染控件：把 Object Tree 输出的 WidgetLayout 用 DrawingVisual 矢量绘制
/// （等宽文本 + 分隔线；Bar/Graph 矢量控件 P1 加入）。
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

        // 先测量每行宽度
        var lineWidths = new double[_layout.Lines.Count];
        var maxWidth = 0.0;
        for (var i = 0; i < _layout.Lines.Count; i++)
        {
            var line = _layout.Lines[i];
            double w = 0;
            if (!line.IsRule)
            {
                foreach (var span in line.Spans)
                    w += Measure(span.Text, typeface, _options.FontSize, dpi);
            }
            lineWidths[i] = w;
            if (w > maxWidth) maxWidth = w;
        }

        var lineHeight = Measure("Ag", typeface, _options.FontSize, dpi);
        var totalHeight = _layout.Lines.Count * lineHeight
            + Math.Max(0, _layout.Lines.Count - 1) * _options.LineGap
            + _options.Padding * 2;

        using (var dc = _visual.RenderOpen())
        {
            var y = _options.Padding;
            for (var i = 0; i < _layout.Lines.Count; i++)
            {
                var line = _layout.Lines[i];
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
                    foreach (var span in line.Spans)
                    {
                        var ft = new FormattedText(span.Text, CultureInfo.InvariantCulture,
                            FlowDirection.LeftToRight, typeface, _options.FontSize, ToBrush(span.Brush), dpi);
                        dc.DrawText(ft, new Point(x, y));
                        x += ft.Width;
                    }
                }
                y += lineHeight + _options.LineGap;
            }
        }

        _measured = new Size(
            Math.Max(maxWidth, 1) + _options.Padding * 2,
            Math.Max(totalHeight, 1));
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
