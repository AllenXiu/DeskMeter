using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace DeskMeter.App;

/// <summary>
/// 系统托盘图标（P2）：菜单 = 编辑配置… / 刷新 / 开机自启（勾选） / 退出。
/// 设置窗口后续接入（设计稿 Settings 三页）。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly WidgetWindow _window;
    private readonly string _configPath;
    private readonly ToolStripMenuItem _autostartItem;

    public TrayIcon(WidgetWindow window, string configPath)
    {
        _window = window;
        _configPath = configPath;

        _icon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "DeskMeter",
            Visible = true,
        };

        var edit = new ToolStripMenuItem("编辑配置…");
        edit.Click += (_, _) => OpenConfigEditor();

        var refresh = new ToolStripMenuItem("刷新");
        refresh.Click += (_, _) => _window.RequestRefresh();

        _autostartItem = new ToolStripMenuItem("开机自启")
        {
            Checked = Autostart.IsEnabled(),
        };
        _autostartItem.Click += (_, _) => ToggleAutostart();

        var exit = new ToolStripMenuItem("退出");
        exit.Click += (_, _) => _window.Close();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[]
        {
            edit, refresh, _autostartItem,
            new ToolStripSeparator(),
            exit,
        });
        _icon.ContextMenuStrip = menu;
        _icon.DoubleClick += (_, _) => OpenConfigEditor();
    }

    private void OpenConfigEditor()
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = "notepad.exe",
                Arguments = _configPath,
                UseShellExecute = true,
            });
        }
        catch
        {
            // 无法打开编辑器时忽略
        }
    }

    private void ToggleAutostart()
    {
        var ok = Autostart.SetEnabled(!_autostartItem.Checked);
        if (ok) _autostartItem.Checked = !_autostartItem.Checked;
    }

    private static System.Drawing.Icon CreateIcon()
    {
        using var bmp = new Bitmap(32, 32);
        using (var g = Graphics.FromImage(bmp))
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.TextRenderingHint = TextRenderingHint.AntiAlias;
            using var brush = new SolidBrush(Color.FromArgb(0x88, 0xCC, 0xFF));
            using var font = new Font("Consolas", 20, FontStyle.Bold, GraphicsUnit.Pixel);
            g.DrawString("D", font, brush, 6, 3);
        }

        var hIcon = bmp.GetHicon();
        try
        {
            using var icon = System.Drawing.Icon.FromHandle(hIcon);
            return (System.Drawing.Icon)icon.Clone();
        }
        finally
        {
            DestroyIcon(hIcon);
        }
    }

    public void Dispose()
    {
        _icon.Visible = false;
        _icon.Dispose();
    }

    [System.Runtime.InteropServices.DllImport("user32.dll")]
    private static extern bool DestroyIcon(IntPtr hIcon);
}
