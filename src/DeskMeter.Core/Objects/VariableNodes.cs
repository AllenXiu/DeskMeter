using DeskMeter.Core.Config;
using DeskMeter.Core.Data;

namespace DeskMeter.Core.Objects;

/// <summary>常量文本节点。</summary>
public sealed class TextNode : ObjectNode
{
    private readonly string _text;

    public TextNode(string text) => _text = text;

    public override void Print(RenderContext ctx) => ctx.Layout.AppendText(_text, ctx.CurrentBrush);
}

/// <summary>换行节点（$newline / 文本中的 \n）。</summary>
public sealed class NewlineNode : ObjectNode
{
    public override void Print(RenderContext ctx) => ctx.Layout.NewLine();
}

/// <summary>水平分隔线（$hr）。</summary>
public sealed class RuleNode : ObjectNode
{
    public override void Print(RenderContext ctx) => ctx.Layout.AppendRule(ctx.CurrentBrush);
}

/// <summary>行内颜色切换（${color} / ${color N} / ${color #hex} / ${color grey}）。</summary>
public sealed class ColorNode : ObjectNode
{
    private readonly string? _spec;
    private readonly ConfigSettings _settings;

    public ColorNode(string? spec, ConfigSettings settings)
    {
        _spec = spec;
        _settings = settings;
    }

    public override void Print(RenderContext ctx) => ctx.CurrentBrush = ColorParser.Parse(_spec, _settings);
}

/// <summary>行内字体切换（${font ...}）——P0 解析但不改变渲染（P1 支持 bold/italic）。</summary>
public sealed class FontNode : ObjectNode
{
    public override void Print(RenderContext ctx) { }
}

/// <summary>${scroll N ...}：P0 静态展开内部变量并显示全部文本，P1 实现循环滚动动画。</summary>
public sealed class ScrollNode : ObjectNode
{
    private readonly List<ObjectNode> _nodes;

    public ScrollNode(string[] args, ObjectRegistry registry, ConfigSettings settings)
    {
        var text = args.Length > 1 ? string.Join(" ", args[1..]) : string.Empty;
        _nodes = ConkyTextParser.Parse(text, registry, settings);
    }

    public override void Print(RenderContext ctx)
    {
        foreach (var node in _nodes) node.Print(ctx);
    }
}

/// <summary>布局对象（alignc/alignr/goto/offset/voffset/tab）——P0 实现空白近似，P1 行级排版。</summary>
public sealed class LayoutNode : ObjectNode
{
    private readonly string _kind;
    private readonly int _n;

    public LayoutNode(string kind, int n)
    {
        _kind = kind;
        _n = n;
    }

    public override void Print(RenderContext ctx)
    {
        switch (_kind)
        {
            case "goto":
                // Conky GOTO 语义：跳到本行第 N 像素列（相对文本区起点），由渲染层处理
                ctx.Layout.AppendGoto(_n);
                break;
            case "offset":
                // offset 为像素相对偏移；P0 用空格近似，P1 改为像素元素
                if (_n > 0) ctx.Layout.AppendText(new string(' ', _n), ctx.CurrentBrush);
                break;
            case "tab":
                if (_n > 0)
                {
                    var width = ctx.Layout.CurrentLine.PlainText.Length;
                    var pad = _n - width % _n;
                    ctx.Layout.AppendText(new string(' ', pad), ctx.CurrentBrush);
                }
                break;
            // alignc / alignr / voffset：P1 行级排版
        }
    }
}

/// <summary>未知/未实现变量节点：输出占位文本（FR-VAR-2），绝不报错。</summary>
public sealed class UnknownVariableNode : ObjectNode
{
    private const string Placeholder = "--";
    private readonly string _name;

    public UnknownVariableNode(string name) => _name = name;

    public override void Print(RenderContext ctx) => ctx.Layout.AppendText(Placeholder, ctx.CurrentBrush);
}

/// <summary>
/// 矢量进度条节点（Conky bar 语义）：输出 WidgetBar 元素，由 WPF 层矢量绘制；
/// Console 后端在 ToConsoleText 中按 Conky console 风格回退为 #/. 字符。
/// </summary>
public sealed class BarNode : ObjectNode
{
    private readonly Func<SystemSnapshot, double> _percent;
    private readonly double _height;
    private readonly double _width;

    /// <summary>Conky 默认：default_bar_height=6、default_bar_width=0（0 = 填满本行剩余宽度）。</summary>
    public BarNode(Func<SystemSnapshot, double> percent, string[] args, ConfigSettings settings)
    {
        _percent = percent;
        var defaultHeight = settings.GetNumber("default_bar_height", 6);
        var defaultWidth = settings.GetNumber("default_bar_width", 0);
        ParseHeightWidth(args, out var h, out var w);
        _height = h > 0 ? h : defaultHeight;
        _width = w > 0 ? w : defaultWidth;
    }

    /// <summary>解析 Conky 参数：\${bar 高度[,宽度]}（如 "6"、"4,120"）。</summary>
    private static void ParseHeightWidth(string[] args, out double height, out double width)
    {
        height = 0;
        width = 0;
        if (args.Length == 0) return;

        var first = args[0].Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (first.Length >= 1 && double.TryParse(first[0],
                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var h))
            height = h;
        if (first.Length >= 2 && double.TryParse(first[1],
                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var w))
            width = w;
        else if (args.Length >= 2 && double.TryParse(args[1],
                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var w2))
            width = w2;
    }

    public override void Print(RenderContext ctx)
    {
        var p = Math.Clamp(_percent(ctx.Data), 0, 100);
        ctx.Layout.AppendBar(p, ctx.CurrentBrush, _height, _width);
    }
}

/// <summary>
/// 矢量曲线图节点（Conky graph 语义）：环形缓冲最近 MaxSamples 个采样点（FR-VIZ-2），
/// 每次 Print 追加当前值并输出 WidgetGraph 元素；WPF 折线+面积渲染，console 用刻度字符回退。
/// </summary>
public sealed class GraphNode : ObjectNode
{
    /// <summary>FR-VIZ-2：曲线图滚动显示最近 N 个采样点（默认 80）。</summary>
    private const int MaxSamples = 80;

    private readonly Func<SystemSnapshot, double> _value;
    private readonly double _height;
    private readonly double _width;
    private readonly double[] _samples = new double[MaxSamples];
    private int _count;

    /// <summary>Conky 默认：default_graph_height=25、default_graph_width=0（0 = 填满本行剩余宽度）。</summary>
    public GraphNode(Func<SystemSnapshot, double> value, string[] args, ConfigSettings settings)
    {
        _value = value;
        var defaultHeight = settings.GetNumber("default_graph_height", 25);
        var defaultWidth = settings.GetNumber("default_graph_width", 0);
        ParseHeightWidth(args, out var h, out var w);
        _height = h > 0 ? h : defaultHeight;
        _width = w > 0 ? w : defaultWidth;
    }

    /// <summary>解析 Conky 参数：\${graph 高度[,宽度]}（如 "32"、"32,260"；-t/-l/-x/-y/-m 旗标 P1 简化忽略）。</summary>
    private static void ParseHeightWidth(string[] args, out double height, out double width)
    {
        height = 0;
        width = 0;
        if (args.Length == 0) return;

        var first = args[0].Split(',', StringSplitOptions.RemoveEmptyEntries);
        if (first.Length >= 1 && double.TryParse(first[0],
                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var h))
            height = h;
        if (first.Length >= 2 && double.TryParse(first[1],
                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var w))
            width = w;
        else if (args.Length >= 2 && double.TryParse(args[1],
                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var w2))
            width = w2;
    }

    public override void Print(RenderContext ctx)
    {
        var v = _value(ctx.Data);
        if (_count < MaxSamples)
        {
            _samples[_count++] = v;
        }
        else
        {
            Array.Copy(_samples, 1, _samples, 0, MaxSamples - 1);
            _samples[MaxSamples - 1] = v;
        }

        var series = new double[_count];
        Array.Copy(_samples, series, _count);
        ctx.Layout.AppendGraph(series, ctx.CurrentBrush, _height, _width);
    }
}

/// <summary>一般变量节点：委托 VariableEvaluator 求值，失败输出占位（FR-VAR-2）。</summary>
public sealed class VariableNode : ObjectNode
{
    private const string Placeholder = "--";
    private readonly string _name;
    private readonly string[] _args;

    public VariableNode(string name, string[] args)
    {
        _name = name;
        _args = args;
    }

    public override void Print(RenderContext ctx)
    {
        var value = VariableEvaluator.Evaluate(_name, _args, ctx.Data, ctx.Settings);
        ctx.Layout.AppendText(value ?? Placeholder, ctx.CurrentBrush);
    }
}
