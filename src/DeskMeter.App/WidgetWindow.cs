using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Interop;
using System.Windows.Threading;
using DeskMeter.Core.Config;
using DeskMeter.Core.Data;
using DeskMeter.Core.Objects;
using DeskMeter.Render;

namespace DeskMeter.App;

/// <summary>
/// 透明置底桌面小部件窗口（P0）：
/// AllowsTransparency + 无边框 + WS_EX_TRANSPARENT（点击穿透）+ WS_EX_NOACTIVATE + HWND_BOTTOM（置底）。
/// </summary>
public sealed class WidgetWindow : Window
{
    private readonly LuaConfigEngine _engine = new();
    private readonly ObjectRegistry _registry = new();
    private readonly SystemDataCollector _collector = new();
    private readonly DispatcherTimer _timer;
    private readonly string _configPath;

    private ConfigSettings _settings;
    private List<ObjectNode> _nodes = new();
    private WidgetVisual _visual = null!;

    public WidgetWindow(string configPath)
    {
        _configPath = configPath;
        LoadConfig();

        WindowStyle = WindowStyle.None;
        AllowsTransparency = true;
        Background = System.Windows.Media.Brushes.Transparent;
        ShowInTaskbar = false;
        ResizeMode = ResizeMode.NoResize;
        Topmost = false;

        _visual = new WidgetVisual(RenderOptions.FromSettings(_settings!));
        Content = _visual;

        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(_settings!.GetUpdateInterval(2.0))
        };
        _timer.Tick += (_, _) => Refresh();
        _timer.Start();

        SourceInitialized += (_, _) => ApplyDesktopWindowStyles();
        Refresh();
    }

    private void LoadConfig()
    {
        var config = _engine.LoadFile(_configPath);
        _settings = config.Settings;
        _nodes = ConkyTextParser.Parse(config.Text, _registry, config.Settings);
    }

    private void Refresh()
    {
        try
        {
            var data = _collector.Collect();
            var layout = new WidgetLayout();
            var ctx = new RenderContext(data, _settings, layout);
            foreach (var node in _nodes) node.Print(ctx);

            _visual.Update(layout, RenderOptions.FromSettings(_settings));
            FitAndPosition(layout);
        }
        catch (Exception ex)
        {
            // FR-CFG-3：刷新失败不影响进程存活，仅记录
            System.Diagnostics.Debug.WriteLine("DeskMeter refresh error: " + ex);
        }
    }

    private void FitAndPosition(WidgetLayout layout)
    {
        var w = _visual.MeasuredSize.Width > 0 ? _visual.MeasuredSize.Width : 200;
        var h = _visual.MeasuredSize.Height > 0 ? _visual.MeasuredSize.Height : 40;
        Width = w;
        Height = h;

        var wa = SystemParameters.WorkArea;
        var gapX = _settings.GetNumber("gap_x", 16);
        var gapY = _settings.GetNumber("gap_y", 16);
        var alignment = _settings.GetAlignment();

        double x, y;
        switch (alignment)
        {
            case WidgetAlignment.TopLeft: x = wa.Left + gapX; y = wa.Top + gapY; break;
            case WidgetAlignment.TopMiddle: x = wa.Left + (wa.Width - w) / 2; y = wa.Top + gapY; break;
            case WidgetAlignment.TopRight: x = wa.Right - w - gapX; y = wa.Top + gapY; break;
            case WidgetAlignment.MiddleLeft: x = wa.Left + gapX; y = wa.Top + (wa.Height - h) / 2; break;
            case WidgetAlignment.MiddleMiddle: x = wa.Left + (wa.Width - w) / 2; y = wa.Top + (wa.Height - h) / 2; break;
            case WidgetAlignment.MiddleRight: x = wa.Right - w - gapX; y = wa.Top + (wa.Height - h) / 2; break;
            case WidgetAlignment.BottomLeft: x = wa.Left + gapX; y = wa.Bottom - h - gapY; break;
            case WidgetAlignment.BottomMiddle: x = wa.Left + (wa.Width - w) / 2; y = wa.Bottom - h - gapY; break;
            default: x = wa.Right - w - gapX; y = wa.Bottom - h - gapY; break;
        }
        Left = x;
        Top = y;
    }

    /// <summary>SourceInitialized 后设置扩展样式（点击穿透/不激活/工具窗口）并置底。</summary>
    private void ApplyDesktopWindowStyles()
    {
        var hwnd = new WindowInteropHelper(this).Handle;
        if (hwnd == IntPtr.Zero) return;

        var ex = GetWindowLong(hwnd, GWL_EXSTYLE);
        ex |= WS_EX_TRANSPARENT | WS_EX_NOACTIVATE | WS_EX_TOOLWINDOW;
        SetWindowLong(hwnd, GWL_EXSTYLE, ex);

        SetWindowPos(hwnd, HWND_BOTTOM, 0, 0, 0, 0,
            SWP_NOMOVE | SWP_NOSIZE | SWP_NOACTIVATE);
    }

    protected override void OnClosed(EventArgs e)
    {
        _collector.Dispose();
        base.OnClosed(e);
    }

    // ---- Win32 ----

    private const int GWL_EXSTYLE = -20;
    private const long WS_EX_TRANSPARENT = 0x00000020;
    private const long WS_EX_NOACTIVATE = 0x08000000;
    private const long WS_EX_TOOLWINDOW = 0x00000080;
    private static readonly IntPtr HWND_BOTTOM = new(1);
    private const uint SWP_NOSIZE = 0x0001;
    private const uint SWP_NOMOVE = 0x0002;
    private const uint SWP_NOACTIVATE = 0x0010;

    [DllImport("user32.dll", SetLastError = true)]
    private static extern long GetWindowLong(IntPtr hWnd, int nIndex);

    [DllImport("user32.dll", SetLastError = true)]
    private static extern long SetWindowLong(IntPtr hWnd, int nIndex, long dwNewLong);

    [DllImport("user32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetWindowPos(IntPtr hWnd, IntPtr hWndInsertAfter,
        int x, int y, int cx, int cy, uint uFlags);
}
