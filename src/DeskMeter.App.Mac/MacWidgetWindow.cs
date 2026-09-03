using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Threading;
using DeskMeter.Core.Config;
using DeskMeter.Core.Data;
using DeskMeter.Core.Objects;
using DeskMeter.Render;

namespace DeskMeter.App.Mac;

public sealed class MacWidgetWindow : Window
{
    private readonly MacSystemDataCollector _collector = new();
    private readonly LuaConfigEngine _engine = new();
    private readonly ObjectRegistry _registry = new();
    private readonly DispatcherTimer _timer;
    private readonly WidgetVisualAvalonia _visual;
    private ConfigSettings _settings = null!;
    private RenderOptionsMac _renderOptions;
    private List<ObjectNode> _nodes = new();
    private int _updateCount;
    private bool _clickThrough = true;
    private string _topSort = "cpu";
    private bool _debugLogged;
    private bool _pinned;
    private TrayIcon? _tray;

    public MacWidgetWindow(CliOptions opts)
    {
        SystemDecorations = SystemDecorations.None;
        TransparencyLevelHint = new[] { WindowTransparencyLevel.Transparent };
        Background = Brushes.Transparent;
        ShowInTaskbar = false;
        CanResize = false;
        Topmost = false;
        SizeToContent = SizeToContent.WidthAndHeight;

        var path = ConfigPathResolver.Resolve(opts.ConfigPath) ?? ConfigPathResolver.WriteFallback();
        LoadConfig(path);
        if (opts.ClickThrough is { } ct) _clickThrough = ct;
        // 层级：默认 pinned 桌面层（壁纸之上、应用之下，不遮挡其它应用）；--window-level overlay 可浮动显示
        _pinned = opts.WindowLevel == "desktop"
            || (opts.WindowLevel != "overlay" && _settings.GetBool("pinned", true));
        _renderOptions = RenderOptionsMac.FromSettings(_settings);
        _visual = new WidgetVisualAvalonia(_renderOptions);
        Content = _visual;

        // Top 表头点击切换排序（click_through=false 时有效）
        _visual.PointerPressed += (_, e) =>
        {
            if (_visual.TopHeaderBounds is { } hb)
            {
                var pos = e.GetPosition(_visual);
                if (pos.X >= hb.X && pos.X <= hb.X + hb.Width && pos.Y >= hb.Y && pos.Y <= hb.Y + hb.Height)
                    CycleTopSort();
            }
        };

        _timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(_settings.GetUpdateInterval(2.0)) };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        Opened += (_, _) =>
        {
            MacWindowHacks.HideFromDock();
            MacWindowHacks.Apply(this, _clickThrough, _pinned);
            if (Environment.GetEnvironmentVariable("DESKMETER_DEBUG_WINDOW") == "1")
            {
                Console.WriteLine($"[win] pos={Position} size={Width}x{Height} visible={IsVisible} pinned={_pinned} clickThrough={_clickThrough}");
                var sc = Screens.ScreenFromWindow(this) ?? Screens.Primary;
                Console.WriteLine("[win] screen=" + (sc is null ? "null" : sc.WorkingArea.ToString()));
            }
            Refresh();
        };
        LayoutUpdated += (_, _) => PositionOnScreen();
        Closed += (_, _) =>
        {
            _timer.Stop();
            _collector.Dispose();
            _tray?.Dispose();
        };
        SetupTray();
    }

    private void LoadConfig(string path)
    {
        var config = _engine.LoadFile(path);
        _settings = config.Settings;
        _topSort = _settings.GetString("top.sort") ?? "cpu";
        _nodes = ConkyTextParser.Parse(config.Text, _registry, config.Settings);
        _clickThrough = _settings.GetBool("click_through", true);
    }

    /// <summary>离屏渲染当前窗口内容为 PNG（无录屏权限环境下的验证手段）。</summary>
    public void SaveSnapshot(string path)
    {
        try
        {
            var size = new PixelSize(Math.Max(1, (int)Math.Round(Width)), Math.Max(1, (int)Math.Round(Height)));
            var rtb = new RenderTargetBitmap(size, new Vector(96, 96));
            rtb.Render(this);
            rtb.Save(path);
            Console.WriteLine("snapshot saved: " + path + " (" + size.Width + "x" + size.Height + ")");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("snapshot failed: " + ex.Message);
        }
    }

    public void Refresh()
    {
        try
        {
            _collector.TopSort = _topSort;
            var data = _collector.Collect();
            var layout = new WidgetLayout();
            var ctx = new RenderContext(data, _settings, layout) { UpdateNumber = ++_updateCount };
            foreach (var node in _nodes) node.Print(ctx);
            _renderOptions = RenderOptionsMac.FromSettings(_settings);
            _visual.Update(layout, _renderOptions);
            if (!_debugLogged)
            {
                _debugLogged = true;
                if (Environment.GetEnvironmentVariable("DESKMETER_DEBUG_MEASURE") == "1")
                    _visual.DebugMeasure();
            }
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("DeskMeter mac refresh: " + ex);
        }
    }

    public void SetTopSort(string sort)
    {
        if (!new[] { "cpu", "mem", "pid", "name", "disk", "gpu", "net" }.Contains(sort)) return;
        _topSort = sort;
        Refresh();
    }

    private void CycleTopSort()
    {
        var order = new[] { "cpu", "mem", "disk", "gpu", "net", "pid", "name" };
        var idx = Array.IndexOf(order, _topSort);
        _topSort = order[(idx + 1) % order.Length];
        Refresh();
    }

    private void PositionOnScreen()
    {
        if (Width <= 0 || Height <= 0) return;
        var screen = Screens.ScreenFromWindow(this) ?? Screens.Primary;
        if (screen is null) return;
        var scale = screen.Scaling > 0 ? screen.Scaling : 1;
        var wa = screen.WorkingArea;
        var gapX = _settings?.GetNumber("gap_x", 16) ?? 16;
        var gapY = _settings?.GetNumber("gap_y", 16) ?? 16;
        var alignment = _settings?.GetAlignment() ?? WidgetAlignment.TopRight;
        double x, y;
        switch (alignment)
        {
            case WidgetAlignment.TopLeft: x = wa.X / scale + gapX; y = wa.Y / scale + gapY; break;
            case WidgetAlignment.TopMiddle: x = (wa.X + wa.Width / 2) / scale - Width / 2; y = wa.Y / scale + gapY; break;
            case WidgetAlignment.TopRight: x = (wa.X + wa.Width) / scale - Width - gapX; y = wa.Y / scale + gapY; break;
            case WidgetAlignment.MiddleLeft: x = wa.X / scale + gapX; y = (wa.Y + wa.Height / 2) / scale - Height / 2; break;
            case WidgetAlignment.MiddleMiddle: x = (wa.X + wa.Width / 2) / scale - Width / 2; y = (wa.Y + wa.Height / 2) / scale - Height / 2; break;
            case WidgetAlignment.MiddleRight: x = (wa.X + wa.Width) / scale - Width - gapX; y = (wa.Y + wa.Height / 2) / scale - Height / 2; break;
            case WidgetAlignment.BottomLeft: x = wa.X / scale + gapX; y = (wa.Y + wa.Height) / scale - Height - gapY; break;
            case WidgetAlignment.BottomMiddle: x = (wa.X + wa.Width / 2) / scale - Width / 2; y = (wa.Y + wa.Height) / scale - Height - gapY; break;
            default: x = (wa.X + wa.Width) / scale - Width - gapX; y = (wa.Y + wa.Height) / scale - Height - gapY; break;
        }
        Position = new PixelPoint((int)Math.Round(x * scale), (int)Math.Round(y * scale));
    }

    private void SetupTray()
    {
        try
        {
            var icon = BuildTrayIcon();
            var menu = new NativeMenu();
            var sort = new NativeMenuItem("Top 排序") { Menu = new NativeMenu() };
            foreach (var (key, label) in new[] { ("cpu", "CPU"), ("mem", "内存"), ("pid", "PID"), ("name", "名称") })
            {
                var item = new NativeMenuItem(label);
                item.Click += (_, _) => SetTopSort(key);
                sort.Menu.Items.Add(item);
            }
            menu.Items.Add(sort);
            var refresh = new NativeMenuItem("立即刷新");
            refresh.Click += (_, _) => Refresh();
            menu.Items.Add(refresh);
            var quit = new NativeMenuItem("退出");
            quit.Click += (_, _) => Close();
            menu.Items.Add(quit);

            _tray = new TrayIcon
            {
                Icon = icon,
                ToolTipText = "DeskMeter (macOS)",
                Menu = menu,
            };
            _tray.IsVisible = true;
        }
        catch (Exception ex)
        {
            System.Diagnostics.Debug.WriteLine("DeskMeter mac tray: " + ex.Message);
        }
    }

    private static WindowIcon BuildTrayIcon()
    {
        const int size = 18;
        var rtb = new RenderTargetBitmap(new PixelSize(size, size), new Vector(96, 96));
        using (var dc = rtb.CreateDrawingContext())
        {
            dc.FillRectangle(new SolidColorBrush(Color.FromRgb(0x22, 0x22, 0x22)), new Rect(0, 0, size, size));
            dc.FillRectangle(Brushes.White, new Rect(2, 2, 3, 3));
            dc.FillRectangle(Brushes.White, new Rect(13, 2, 3, 3));
            dc.FillRectangle(Brushes.White, new Rect(2, 13, 3, 3));
            dc.FillRectangle(Brushes.White, new Rect(13, 13, 3, 3));
            dc.DrawLine(new Pen(Brushes.White, 2), new Point(5, 9), new Point(13, 9));
        }
        return new WindowIcon(rtb);
    }
}
