using System.Diagnostics;
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

/// <summary>换行节点（$newline / 文本中的 \n）。规则行后紧跟的换行是空操作（Conky：$hr 已占一行）。</summary>
public sealed class NewlineNode : ObjectNode
{
    public override void Print(RenderContext ctx)
    {
        // $hr\n：规则行自身已占一行，换行不再新建空行（避免空白间隙）
        if (ctx.Layout.CurrentLine.IsRule) return;
        ctx.Layout.NewLine();
    }
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

/// <summary>
/// ${scroll N ...}：Conky 语义——[left|right|wait] 长度 [步长] [间隔] 文本；
/// 文本超过长度时前缀补空格向左滚动（默认），每次刷新前进 step 字符，到尾回绕。
/// </summary>
public sealed class ScrollNode : ObjectNode
{
    private readonly List<ObjectNode> _nodes;
    private readonly int _show;
    private readonly int _step;
    private int _start;

    public ScrollNode(string[] args, ObjectRegistry registry, ConfigSettings settings)
    {
        _step = 1;
        var i = 0;
        if (args.Length > 0 && args[0] is "left" or "l") i = 1;
        else if (args.Length > 0 && args[0] is "right" or "r" or "wait" or "w")
        {
            // 右向/等待 P1 简化：按左向处理（默认方向与官方配置一致）
            i = 1;
        }
        _show = i < args.Length && int.TryParse(args[i], out var show) ? Math.Max(1, show) : 32;
        i++;
        // 可选 step / interval 仅在能解析为数字时消耗，否则属于文本（Conky sscanf 语义）
        if (i < args.Length && int.TryParse(args[i], out var step))
        {
            _step = Math.Max(1, step);
            i++;
            if (i < args.Length && int.TryParse(args[i], out _)) i++;
        }
        var text = string.Join(" ", args[i..]);
        _nodes = ConkyTextParser.Parse(text, registry, settings);
    }

    public override void Print(RenderContext ctx)
    {
        // 1) 展开嵌套变量为纯文本（Conky generate_text_internal 等价物）
        var temp = new WidgetLayout();
        var tempCtx = new RenderContext(ctx.Data, ctx.Settings, temp);
        foreach (var node in _nodes) node.Print(tempCtx);
        var full = string.Concat(temp.Lines.SelectMany(l => l.Elements.OfType<WidgetText>().Select(t => t.Text)));
        full = full.Replace('\n', '|'); // Conky LINESEPARATOR

        // 2) 文本不超过长度 → 静态显示
        if (full.Length <= _show)
        {
            ctx.Layout.AppendText(full, ctx.CurrentBrush);
            return;
        }

        // 3) 前缀补 _show 个空格，窗口左移
        var padded = new string(' ', _show) + full;
        _start += _step;
        if (_start >= padded.Length) _start = 0;
        var len = Math.Min(_show, padded.Length - _start);
        ctx.Layout.AppendText(padded.Substring(_start, len), ctx.CurrentBrush);
    }
}

/// <summary>
/// \${exec 命令} / \${execpi N 命令}：异步执行命令并显示 stdout（FR-LATER.exec）。
/// exec 每次刷新执行；execpi 每 N 秒执行一次。3s 超时；未完成/失败显示占位或保留上次输出，
/// 不阻塞主循环（update_cb 等价物）。
/// </summary>
public sealed class ExecNode : ObjectNode
{
    private const string Placeholder = "--";
    private const int MaxOutputLength = 1024;
    private static readonly TimeSpan Timeout = TimeSpan.FromSeconds(3);

    private readonly string _command;
    private readonly TimeSpan? _interval;
    private readonly object _lock = new();
    private string? _output;
    private DateTime _lastStart;
    private bool _running;

    public ExecNode(string[] args, bool periodic)
    {
        if (periodic && args.Length >= 2 &&
            double.TryParse(args[0], System.Globalization.NumberStyles.Any,
                System.Globalization.CultureInfo.InvariantCulture, out var seconds) && seconds > 0)
        {
            _interval = TimeSpan.FromSeconds(seconds);
            _command = string.Join(" ", args[1..]);
        }
        else
        {
            _command = string.Join(" ", args);
        }
    }

    public override void Print(RenderContext ctx)
    {
        lock (_lock)
        {
            var due = _output is null || _interval is null ||
                      DateTime.UtcNow - _lastStart >= _interval.Value;
            if (due && !_running)
            {
                _running = true;
                _lastStart = DateTime.UtcNow;
                _ = RunAsync();
            }
            ctx.Layout.AppendText(_output ?? Placeholder, ctx.CurrentBrush);
        }
    }

    private async Task RunAsync()
    {
        Process? process = null;
        try
        {
            using var cts = new CancellationTokenSource(Timeout);
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + _command,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            process = Process.Start(psi);
            if (process is null) return;

            var stdout = await process.StandardOutput.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
            var text = stdout.TrimEnd('\r', '\n');
            lock (_lock)
            {
                _output = text.Length > MaxOutputLength ? text[..MaxOutputLength] : text;
            }
        }
        catch (OperationCanceledException)
        {
            try { process?.Kill(entireProcessTree: true); } catch { /* 已退出 */ }
        }
        catch
        {
            // 启动/读取失败：保留上次输出（无则占位）
        }
        finally
        {
            lock (_lock) _running = false;
        }
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
            case "alignc":
                ctx.Layout.AppendAlignC();
                break;
            case "alignr":
                ctx.Layout.AppendAlignR(_n);
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
