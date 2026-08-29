using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using DeskMeter.Core.Config;

namespace DeskMeter.App;

/// <summary>
/// 系统托盘图标（P2）：菜单 = 配置▶ / 设置… / 退出
/// （用户决策：编辑配置… / 刷新 / 开机自启 从托盘移除；开机自启仍在设置窗口常规页，编辑配置在设置窗口配置页）。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly WidgetWindow _window;
    private readonly string _configPath;
    private readonly ConfigManager _configs;
    private readonly ToolStripMenuItem _configMenu;

    public TrayIcon(WidgetWindow window, string configPath, ConfigManager configs)
    {
        _window = window;
        _configPath = configPath;
        _configs = configs;

        _icon = new NotifyIcon
        {
            Icon = CreateIcon(),
            Text = "DeskMeter",
            Visible = true,
        };

        var settings = new ToolStripMenuItem("设置…");
        settings.Click += (_, _) => SettingsLauncher.Open(_configPath, _configs);

        _configMenu = new ToolStripMenuItem("配置▶");

        var exit = new ToolStripMenuItem("退出");
        exit.Click += (_, _) => _window.Close();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[]
        {
            _configMenu, settings,
            new ToolStripSeparator(),
            exit,
        });
        // 每次弹出菜单：重建配置子菜单（勾选当前配置）
        menu.Opening += (_, _) => RebuildConfigMenu();
        _icon.ContextMenuStrip = menu;
    }

    /// <summary>重建"配置▶"子菜单：列出配置库，勾选当前项，点击切换；附"导入配置…"。</summary>
    private void RebuildConfigMenu()
    {
        _configMenu.DropDownItems.Clear();
        try
        {
            var currentName = _configs.Current()?.Name;
            foreach (var entry in _configs.List())
            {
                var item = new ToolStripMenuItem(entry.Name)
                {
                    Checked = string.Equals(entry.Name, currentName, StringComparison.OrdinalIgnoreCase),
                };
                item.Click += (_, _) => SwitchTo(entry);
                _configMenu.DropDownItems.Add(item);
            }
        }
        catch { }

        if (_configMenu.DropDownItems.Count > 0)
            _configMenu.DropDownItems.Add(new ToolStripSeparator());
        var import = new ToolStripMenuItem("导入配置…");
        import.Click += (_, _) => ImportConfig();
        _configMenu.DropDownItems.Add(import);
    }

    private void SwitchTo(ConfigEntry entry)
    {
        if (_configs.SetCurrent(entry))
            _window.SwitchConfig(entry.Path);
    }

    private void ImportConfig()
    {
        try
        {
            using var dialog = new System.Windows.Forms.OpenFileDialog
            {
                Title = "导入 DeskMeter 配置",
                Filter = "Conky 配置 (*.conf)|*.conf|所有文件 (*.*)|*.*",
            };
            if (dialog.ShowDialog() != System.Windows.Forms.DialogResult.OK) return;
            var entry = _configs.Import(dialog.FileName);
            if (entry is null)
            {
                System.Windows.Forms.MessageBox.Show("导入失败", "DeskMeter",
                    System.Windows.Forms.MessageBoxButtons.OK, System.Windows.Forms.MessageBoxIcon.Warning);
                return;
            }
            SwitchTo(entry);
        }
        catch { }
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
