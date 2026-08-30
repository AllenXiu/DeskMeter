using System.Diagnostics;
using DeskMeter.Core.Config;
using DeskMeter.Core.Data;

namespace DeskMeter.Core.Objects;

/// <summary>常量文本节点。</summary>
public sealed class TextNode : ObjectNode
{
    private readonly string _text;

    public TextNode(string text) => _text = text;

    public override void Print(RenderContext ctx) => ctx.Layout.AppendText(_text, ctx.CurrentBrush, ctx.CurrentFont);
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

/// <summary>行内字体切换（${font 家族:size=字号}）；无参数恢复配置默认字体。</summary>
public sealed class FontNode : ObjectNode
{
    private readonly FontSpec? _font;
    private readonly bool _reset;

    public FontNode(string[] args)
    {
        var spec = args.Length > 0 ? string.Join(" ", args) : string.Empty;
        _reset = string.IsNullOrWhiteSpace(spec);
        _font = _reset ? null : FontSpec.Parse(spec);
    }

    public override void Print(RenderContext ctx) => ctx.CurrentFont = _reset ? null : _font;
}

/// <summary>
/// ${scroll [left|right|wait] 长度 [步长] [间隔] 文本}：
/// 文本超过长度时滚动显示。left = 前缀补空格向左滚动（默认）；right = 从尾部向右滚入；
/// wait = 向左滚动到尾后停留一段时间再回绕。step 每次刷新前进字符数，interval 每 N 次刷新前进一次。
/// </summary>
public sealed class ScrollNode : ObjectNode
{
    private readonly List<ObjectNode> _nodes;
    private readonly int _show;
    private readonly int _step;
    private readonly int _interval;
    private readonly bool _right;
    private readonly bool _wait;
    private int _start;
    private int _framesSinceStep;
    private int _waitFrames;

    public ScrollNode(string[] args, ObjectRegistry registry, ConfigSettings settings)
    {
        _step = 1;
        _interval = 1;
        var i = 0;
        if (args.Length > 0 && args[0] is "right" or "r")
        {
            _right = true;
            i = 1;
        }
        else if (args.Length > 0 && args[0] is "wait" or "w")
        {
            _wait = true;
            i = 1;
        }
        else if (args.Length > 0 && args[0] is "left" or "l")
        {
            i = 1;
        }
        _show = i < args.Length && int.TryParse(args[i], out var show) ? Math.Max(1, show) : 32;
        i++;
        // 可选 step / interval 仅在能解析为数字时消耗，否则属于文本（Conky sscanf 语义）
        if (i < args.Length && int.TryParse(args[i], out var step))
        {
            _step = Math.Max(1, step);
            i++;
            if (i < args.Length && int.TryParse(args[i], out var interval) && interval > 0)
            {
                _interval = interval;
                i++;
            }
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
            ctx.Layout.AppendText(full, ctx.CurrentBrush, ctx.CurrentFont);
            return;
        }

        // 3) 左右滚动：left = 前缀补空格（内容从右入）；right = 后缀补空格（内容从右出、尾部先见）
        var padded = _right ? full + new string(' ', _show) : new string(' ', _show) + full;

        if (++_framesSinceStep >= _interval)
        {
            _framesSinceStep = 0;
            var maxStart = padded.Length - _show;
            if (_wait)
            {
                // 到尾停留 _show 帧后回绕
                if (_start >= maxStart)
                {
                    if (++_waitFrames >= _show) { _start = 0; _waitFrames = 0; }
                }
                else
                {
                    _start = Math.Min(maxStart, _start + _step);
                }
            }
            else
            {
                _start += _step;
                if (_start >= padded.Length) _start = 0;
            }
        }

        var len = Math.Min(_show, padded.Length - _start);
        ctx.Layout.AppendText(padded.Substring(_start, len), ctx.CurrentBrush, ctx.CurrentFont);
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
            ctx.Layout.AppendText(_output ?? Placeholder, ctx.CurrentBrush, ctx.CurrentFont);
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

/// <summary>布局对象（alignc/alignr/goto/offset/voffset/tab）——像素语义由渲染层处理，console 忽略。</summary>
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
                // offset = 像素相对偏移（可为负向左），由渲染层移动绘制位置
                ctx.Layout.AppendOffset(_n);
                break;
            case "voffset":
                // voffset = 像素垂直偏移，本行后续内容下移
                ctx.Layout.AppendVOffset(_n);
                break;
            case "tab":
                // tab = 像素制表位：前进到下一个 N 像素整倍列（渲染层处理）
                ctx.Layout.AppendTab(_n);
                break;
            // alignc / alignr：行级排版由渲染层处理
        }
    }
}

/// <summary>未知/未实现变量节点：输出占位文本（FR-VAR-2），绝不报错。</summary>
public sealed class UnknownVariableNode : ObjectNode
{
    private const string Placeholder = "--";
    private readonly string _name;

    public UnknownVariableNode(string name) => _name = name;

    public override void Print(RenderContext ctx) => ctx.Layout.AppendText(Placeholder, ctx.CurrentBrush, ctx.CurrentFont);
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
    private readonly double _yScale = 1;
    private readonly double? _maxOverride;
    private readonly bool _logScale;
    private readonly int _interval = 1;
    private int _sampleCounter;
    private readonly double[] _samples = new double[MaxSamples];
    private int _count;

    /// <summary>Conky 默认：default_graph_height=25、default_graph_width=0（0 = 填满本行剩余宽度）。</summary>
    public GraphNode(Func<SystemSnapshot, double> value, string[] args, ConfigSettings settings)
    {
        _value = value;
        var defaultHeight = settings.GetNumber("default_graph_height", 25);
        var defaultWidth = settings.GetNumber("default_graph_width", 0);

        // 旗标：-l 对数、-m <max> 固定最大值、-i <interval> 采样间隔、-y <scale> 纵轴倍率、-t/-x 解析忽略
        var rest = new List<string>();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "-l": _logScale = true; break;
                case "-t": break;
                case "-m" when i + 1 < args.Length && double.TryParse(args[++i],
                    System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var m) && m > 0:
                    _maxOverride = m;
                    break;
                case "-i" when i + 1 < args.Length && int.TryParse(args[++i], out var iv) && iv > 0:
                    _interval = iv;
                    break;
                case "-y" when i + 1 < args.Length && double.TryParse(args[++i],
                    System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var ys) && ys > 0:
                    _yScale = ys;
                    break;
                case "-x" when i + 1 < args.Length:
                    i++; // X 轴倍率：简化忽略
                    break;
                default:
                    rest.Add(args[i]);
                    break;
            }
        }
        ParseHeightWidth(rest.ToArray(), out var h, out var w);
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
        if (_sampleCounter++ % _interval == 0)
        {
            var v = _value(ctx.Data) * _yScale;
            if (_count < MaxSamples)
            {
                _samples[_count++] = v;
            }
            else
            {
                Array.Copy(_samples, 1, _samples, 0, MaxSamples - 1);
                _samples[MaxSamples - 1] = v;
            }
        }

        var series = new double[_count];
        Array.Copy(_samples, series, _count);
        ctx.Layout.AppendGraph(series, ctx.CurrentBrush, _height, _width, _maxOverride, _logScale);
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
        ctx.Layout.AppendText(value ?? Placeholder, ctx.CurrentBrush, ctx.CurrentFont);
    }
}

/// <summary>
/// 条件块节点（${if_xxx ...}...${else}...${endif}）：运行时求值条件，选择 then/else 子树输出。
/// 支持 if_existing / if_mounted / if_match / if_running / if_up / if_empty / if_updatenr / if_gw。
/// </summary>
public sealed class ConditionalNode : ObjectNode
{
    private readonly string _kind;
    private readonly string[] _rawArgs;
    private readonly List<ObjectNode> _thenNodes;
    private readonly List<ObjectNode> _elseNodes;
    private readonly ObjectRegistry _registry;
    private readonly ConfigSettings _settings;

    public ConditionalNode(string kind, string[] rawArgs, List<ObjectNode> thenNodes, List<ObjectNode> elseNodes,
        ObjectRegistry registry, ConfigSettings settings)
    {
        _kind = kind.ToLowerInvariant();
        _rawArgs = rawArgs;
        _thenNodes = thenNodes;
        _elseNodes = elseNodes;
        _registry = registry;
        _settings = settings;
    }

    public override void Print(RenderContext ctx)
    {
        var hit = Evaluate(ctx);
        var nodes = hit ? _thenNodes : _elseNodes;
        foreach (var node in nodes) node.Print(ctx);
    }

    private bool Evaluate(RenderContext ctx)
    {
        try
        {
            var a = _rawArgs.Select(x => Expand(ctx, x)).ToArray();
            switch (_kind)
            {
                case "if_existing":
                    return a.Length > 0 && (System.IO.File.Exists(a[0]) || System.IO.Directory.Exists(a[0]));
                case "if_mounted":
                {
                    if (a.Length == 0) return false;
                    var root = SystemSnapshot.NormalizeDiskPath(a[0]);
                    try { return System.IO.DriveInfo.GetDrives().Any(d => d.IsReady && string.Equals(d.RootDirectory.FullName, root, StringComparison.OrdinalIgnoreCase)); }
                    catch { return false; }
                }
                case "if_match":
                    return Match(string.Join(" ", a));
                case "if_running":
                {
                    if (a.Length == 0) return false;
                    var name = a[0];
                    if (name.EndsWith(".exe", StringComparison.OrdinalIgnoreCase)) name = name[..^4];
                    try
                    {
                        var procs = System.Diagnostics.Process.GetProcessesByName(name);
                        try { return procs.Length > 0; }
                        finally { foreach (var proc in procs) proc.Dispose(); }
                    }
                    catch { return false; }
                }
                case "if_up":
                {
                    var want = a.Length > 0 ? a[0] : string.Empty;
                    try
                    {
                        foreach (var ni in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                        {
                            if (ni.NetworkInterfaceType == System.Net.NetworkInformation.NetworkInterfaceType.Loopback) continue;
                            if (ni.OperationalStatus != System.Net.NetworkInformation.OperationalStatus.Up) continue;
                            if (want.Length == 0 || string.Equals(ni.Name, want, StringComparison.OrdinalIgnoreCase)) return true;
                        }
                        return false;
                    }
                    catch { return false; }
                }
                case "if_empty":
                    return a.Length == 0 || string.IsNullOrEmpty(a[0]);
                case "if_updatenr":
                    return int.TryParse(a.Length > 0 ? a[0] : string.Empty, out var n) && ctx.UpdateNumber == n;
                case "if_gw":
                    if (a.Length == 0) return false;
                    return ctx.Data.GatewayIps.Any(g => string.Equals(g, a[0], StringComparison.OrdinalIgnoreCase));
                default:
                    return false;
            }
        }
        catch
        {
            return false; // 条件求值失败按 false 处理（FR-VAR-2 容错）
        }
    }

    /// <summary>展开参数中的嵌套变量为纯文本（如 ${if_match ${cpu} > 50}）。</summary>
    private string Expand(RenderContext ctx, string arg)
    {
        if (!arg.Contains("${", StringComparison.Ordinal)) return arg;
        var temp = new WidgetLayout();
        var tempCtx = new RenderContext(ctx.Data, ctx.Settings, temp)
        {
            CurrentBrush = ctx.CurrentBrush,
            CurrentFont = ctx.CurrentFont,
            UpdateNumber = ctx.UpdateNumber,
        };
        foreach (var node in ConkyTextParser.Parse(arg, _registry, _settings)) node.Print(tempCtx);
        return string.Concat(temp.Lines.SelectMany(l => l.Elements.OfType<WidgetText>().Select(t => t.Text)));
    }

    /// <summary>if_match 表达式：支持 == != > < >= <=，数值优先，否则字符串比较。</summary>
    private static bool Match(string expr)
    {
        var parts = expr.Split(new[] { "==", "!=", ">=", "<=", ">", "<" }, StringSplitOptions.None);
        if (parts.Length != 2) return false;
        var left = parts[0].Trim();
        var right = parts[1].Trim();
        var op = expr.Contains("==") ? "==" : expr.Contains("!=") ? "!=" :
                 expr.Contains(">=") ? ">=" : expr.Contains("<=") ? "<=" :
                 expr.Contains(">") ? ">" : expr.Contains("<") ? "<" : string.Empty;
        if (op.Length == 0) return false;
        if (double.TryParse(left, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var ln) &&
            double.TryParse(right, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var rn))
        {
            return op switch
            {
                "==" => Math.Abs(ln - rn) < 1e-9,
                "!=" => Math.Abs(ln - rn) >= 1e-9,
                ">" => ln > rn,
                "<" => ln < rn,
                ">=" => ln >= rn,
                _ => ln <= rn,
            };
        }
        var cmp = string.CompareOrdinal(left, right);
        return op switch
        {
            "==" => cmp == 0,
            "!=" => cmp != 0,
            ">" => cmp > 0,
            "<" => cmp < 0,
            ">=" => cmp >= 0,
            _ => cmp <= 0,
        };
    }
}
/// <summary>
/// 文本处理变量（Conky 纯字符串操作，Windows 无关）：
/// words / uppercase / lowercase / startcase / rstrip / eval / to_bytes / combine / lines / head / tail。
/// 参数先展开嵌套变量再处理（与 Conky generate_text_internal 一致）。
/// </summary>
public sealed class TextOpNode : ObjectNode
{
    private readonly string _kind;
    private readonly string[] _args;
    private readonly ObjectRegistry _registry;
    private readonly ConfigSettings _settings;

    public TextOpNode(string kind, string[] args, ObjectRegistry registry, ConfigSettings settings)
    {
        _kind = kind;
        _args = args;
        _registry = registry;
        _settings = settings;
    }

    public override void Print(RenderContext ctx)
    {
        try
        {
            var expanded = _args.Select(a => Expand(ctx, a)).ToArray();
            switch (_kind)
            {
                case "words":
                    ctx.Layout.AppendText(expanded.Length == 0 ? "0" : string.Join(" ", expanded)
                        .Split(new[] { ' ', '\t', '\n', '\r' }, StringSplitOptions.RemoveEmptyEntries).Length.ToString(),
                        ctx.CurrentBrush, ctx.CurrentFont);
                    break;
                case "uppercase":
                    ctx.Layout.AppendText(string.Join(" ", expanded).ToUpperInvariant(), ctx.CurrentBrush, ctx.CurrentFont);
                    break;
                case "lowercase":
                    ctx.Layout.AppendText(string.Join(" ", expanded).ToLowerInvariant(), ctx.CurrentBrush, ctx.CurrentFont);
                    break;
                case "startcase":
                {
                    var txt = string.Join(" ", expanded);
                    var sb = new System.Text.StringBuilder(txt.Length);
                    var prevSpace = true;
                    foreach (var ch in txt)
                    {
                        sb.Append(prevSpace && char.IsLetter(ch) ? char.ToUpperInvariant(ch) : ch);
                        prevSpace = char.IsWhiteSpace(ch);
                    }
                    ctx.Layout.AppendText(sb.ToString(), ctx.CurrentBrush, ctx.CurrentFont);
                    break;
                }
                case "rstrip":
                    ctx.Layout.AppendText(string.Join(" ", expanded).TrimEnd(), ctx.CurrentBrush, ctx.CurrentFont);
                    break;
                case "eval":
                    ctx.Layout.AppendText(Expand(ctx, string.Join(" ", _args)), ctx.CurrentBrush, ctx.CurrentFont);
                    break;
                case "to_bytes":
                    ctx.Layout.AppendText(ToBytes(expanded.Length > 0 ? expanded[0] : string.Empty), ctx.CurrentBrush, ctx.CurrentFont);
                    break;
                case "combine":
                    ctx.Layout.AppendText(expanded.Length > 1 ? string.Join(expanded[0], expanded.Skip(1)) : string.Empty, ctx.CurrentBrush, ctx.CurrentFont);
                    break;
                case "lines":
                    ctx.Layout.AppendText(expanded.Length > 0 && System.IO.File.Exists(expanded[0])
                        ? System.IO.File.ReadLines(expanded[0]).Count().ToString() : "--", ctx.CurrentBrush, ctx.CurrentFont);
                    break;
                case "head":
                case "tail":
                    AppendFileLines(ctx, expanded, _kind == "tail");
                    break;
            }
        }
        catch
        {
            ctx.Layout.AppendText("--", ctx.CurrentBrush, ctx.CurrentFont);
        }
    }

    private void AppendFileLines(RenderContext ctx, string[] expanded, bool tail)
    {
        if (expanded.Length < 2 || !int.TryParse(expanded[0], out var count) || !System.IO.File.Exists(expanded[1]))
        {
            ctx.Layout.AppendText("--", ctx.CurrentBrush, ctx.CurrentFont);
            return;
        }
        var lines = System.IO.File.ReadLines(expanded[1]).ToList();
        var range = tail ? lines.Skip(Math.Max(0, lines.Count - Math.Max(1, count))) : lines.Take(Math.Max(1, count));
        var first = true;
        foreach (var line in range)
        {
            if (!first) ctx.Layout.NewLine();
            ctx.Layout.AppendText(line, ctx.CurrentBrush, ctx.CurrentFont);
            first = false;
        }
    }

    private static string ToBytes(string s)
    {
        s = s.Trim();
        if (double.TryParse(s, System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var plain))
            return ((long)plain).ToString(System.Globalization.CultureInfo.InvariantCulture);
        var m = System.Text.RegularExpressions.Regex.Match(s, @"^([0-9.]+)\s*(B|K|KB|KiB|M|MB|MiB|G|GB|GiB|T|TB|TiB)?$",
            System.Text.RegularExpressions.RegexOptions.IgnoreCase);
        if (!m.Success) return "--";
        var v = double.Parse(m.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
        var mult = m.Groups[2].Value.ToUpperInvariant() switch
        {
            "B" or "" => 1L,
            "K" or "KB" or "KIB" => 1024L,
            "M" or "MB" or "MIB" => 1024L * 1024,
            "G" or "GB" or "GIB" => 1024L * 1024 * 1024,
            "T" or "TB" or "TIB" => 1024L * 1024 * 1024 * 1024,
            _ => 1L,
        };
        return ((long)(v * mult)).ToString(System.Globalization.CultureInfo.InvariantCulture);
    }

    private string Expand(RenderContext ctx, string arg)
    {
        if (!arg.Contains("${", StringComparison.Ordinal)) return arg;
        var temp = new WidgetLayout();
        var tempCtx = new RenderContext(ctx.Data, ctx.Settings, temp)
        {
            CurrentBrush = ctx.CurrentBrush,
            CurrentFont = ctx.CurrentFont,
            UpdateNumber = ctx.UpdateNumber,
            LuaScript = ctx.LuaScript,
        };
        foreach (var node in ConkyTextParser.Parse(arg, _registry, _settings)) node.Print(tempCtx);
        return string.Concat(temp.Lines.SelectMany(l => l.Elements.OfType<WidgetText>().Select(t => t.Text)));
    }
}

/// <summary>命令输出缓存（execbar/execgraph 用；与 ExecNode 相同异步语义）。</summary>
internal sealed class ExecOutputCache
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

    public ExecOutputCache(string command, TimeSpan? interval)
    {
        _command = command;
        _interval = interval;
    }

    public string Get()
    {
        lock (_lock)
        {
            var due = _output is null || _interval is null || DateTime.UtcNow - _lastStart >= _interval.Value;
            if (due && !_running)
            {
                _running = true;
                _lastStart = DateTime.UtcNow;
                _ = RunAsync();
            }
            return _output ?? Placeholder;
        }
    }

    private async Task RunAsync()
    {
        System.Diagnostics.Process? process = null;
        try
        {
            using var cts = new CancellationTokenSource(Timeout);
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = "/c " + _command,
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true,
            };
            process = System.Diagnostics.Process.Start(psi);
            if (process is null) return;
            var stdout = await process.StandardOutput.ReadToEndAsync(cts.Token);
            await process.WaitForExitAsync(cts.Token);
            var text = stdout.TrimEnd('\r', '\n');
            lock (_lock) _output = text.Length > MaxOutputLength ? text[..MaxOutputLength] : text;
        }
        catch (OperationCanceledException)
        {
            try { process?.Kill(entireProcessTree: true); } catch { }
        }
        catch { }
        finally
        {
            lock (_lock) _running = false;
        }
    }
}

/// <summary>
/// execbar / execgauge / execibar / execigauge（命令输出首数字 0-100 → 矢量进度条）与
/// execgraph / execigraph（命令输出 → 曲线图采样）。
/// </summary>
public sealed class ExecBarGraphNode : ObjectNode
{
    private readonly ExecOutputCache _cache;
    private readonly double _height;
    private readonly double _width;
    private readonly bool _graph;
    private readonly double[] _samples = new double[80];
    private int _count;

    public ExecBarGraphNode(string[] args, ConfigSettings settings, bool graph, bool periodic)
    {
        _graph = graph;
        string command;
        TimeSpan? interval = null;
        if (periodic && args.Length >= 2 && double.TryParse(args[0],
                System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var sec) && sec > 0)
        {
            interval = TimeSpan.FromSeconds(sec);
            command = string.Join(" ", args[1..]);
        }
        else
        {
            command = string.Join(" ", args);
        }
        _cache = new ExecOutputCache(command, interval);
        var defaultHeight = settings.GetNumber(graph ? "default_graph_height" : "default_bar_height", graph ? 25 : 6);
        var defaultWidth = settings.GetNumber(graph ? "default_graph_width" : "default_bar_width", 0);
        _height = defaultHeight;
        _width = defaultWidth;
    }

    public override void Print(RenderContext ctx)
    {
        var output = _cache.Get();
        var v = double.TryParse(output?.Trim().Split(' ')[0] ?? string.Empty,
            System.Globalization.NumberStyles.Any, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : 0;
        v = Math.Clamp(v, 0, 100);
        if (!_graph)
        {
            ctx.Layout.AppendBar(v, ctx.CurrentBrush, _height, _width);
            return;
        }
        if (_count < _samples.Length) _samples[_count++] = v;
        else
        {
            Array.Copy(_samples, 1, _samples, 0, _samples.Length - 1);
            _samples[_samples.Length - 1] = v;
        }
        var series = new double[_count];
        Array.Copy(_samples, series, _count);
        ctx.Layout.AppendGraph(series, ctx.CurrentBrush, _height, _width);
    }
}

/// <summary>${lua 函数 [参数...]}：调用 conky.config 里定义的 Lua 函数并显示返回值。</summary>
public sealed class LuaNode : ObjectNode
{
    private readonly string _func;
    private readonly string[] _args;
    private readonly ObjectRegistry _registry;
    private readonly ConfigSettings _settings;

    public LuaNode(string[] args, ObjectRegistry registry, ConfigSettings settings)
    {
        _func = args.Length > 0 ? args[0] : string.Empty;
        _args = args.Length > 1 ? args[1..] : Array.Empty<string>();
        _registry = registry;
        _settings = settings;
    }

    public override void Print(RenderContext ctx)
    {
        if (string.IsNullOrEmpty(_func) || ctx.LuaScript is not MoonSharp.Interpreter.Script script)
        {
            ctx.Layout.AppendText("--", ctx.CurrentBrush, ctx.CurrentFont);
            return;
        }
        try
        {
            var fn = script.Globals.Get(_func);
            if (fn.IsNil())
            {
                ctx.Layout.AppendText("--", ctx.CurrentBrush, ctx.CurrentFont);
                return;
            }
            var callArgs = _args.Select(a => MoonSharp.Interpreter.DynValue.NewString(Expand(ctx, a))).ToArray();
            var result = script.Call(fn, callArgs);
            var text = result.IsNil() ? string.Empty : result.CastToString() ?? result.ToPrintString();
            ctx.Layout.AppendText(text, ctx.CurrentBrush, ctx.CurrentFont);
        }
        catch
        {
            ctx.Layout.AppendText("--", ctx.CurrentBrush, ctx.CurrentFont);
        }
    }

    private string Expand(RenderContext ctx, string arg)
    {
        if (!arg.Contains("${", StringComparison.Ordinal)) return arg;
        var temp = new WidgetLayout();
        var tempCtx = new RenderContext(ctx.Data, ctx.Settings, temp)
        {
            CurrentBrush = ctx.CurrentBrush,
            CurrentFont = ctx.CurrentFont,
            UpdateNumber = ctx.UpdateNumber,
            LuaScript = ctx.LuaScript,
        };
        foreach (var node in ConkyTextParser.Parse(arg, _registry, _settings)) node.Print(tempCtx);
        return string.Concat(temp.Lines.SelectMany(l => l.Elements.OfType<WidgetText>().Select(t => t.Text)));
    }
}
/// <summary>
/// ${top_header}：按 deskmeter.top.columns 输出 Top 表头行（含排序切换标记，渲染层记录区域）。
/// 列：name/pid/cpu/mem/disk/disk_read/disk_write/gpu/net；默认 name,pid,cpu,mem。
/// </summary>
public sealed class TopHeaderNode : ObjectNode
{
    private readonly ConfigSettings _settings;

    public TopHeaderNode(ConfigSettings settings) => _settings = settings;

    public override void Print(RenderContext ctx)
    {
        var cols = _settings.GetStringList("top.columns");
        if (cols.Count == 0) cols = new[] { "name", "pid", "cpu", "mem" };
        var nameWidth = (int)_settings.GetNumber("top_name_width", 15) + 1;
        var sb = new System.Text.StringBuilder();
        foreach (var c in cols)
        {
            sb.Append(c.ToLowerInvariant() switch
            {
                "name" => "Name".PadRight(nameWidth),
                "pid" => "PID".PadLeft(7),
                "cpu" => "CPU%".PadLeft(6),
                "mem" => "MEM%".PadLeft(6),
                "disk" => "Disk".PadLeft(12),
                "disk_read" => "R".PadLeft(12),
                "disk_write" => "W".PadLeft(12),
                "gpu" => "GPU%".PadLeft(6),
                "net" => "Net".PadLeft(6),
                _ => c,
            });
        }
        ctx.Layout.AppendText(sb.ToString(), ctx.CurrentBrush, ctx.CurrentFont, isTopHeader: true);
    }
}