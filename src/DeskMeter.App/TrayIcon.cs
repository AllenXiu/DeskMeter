using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;
using DeskMeter.Core.Config;

namespace DeskMeter.App;

/// <summary>
/// 系统托盘图标（设置界面已移除，全部功能移至托盘，用户决策）：
/// 配置▶（切换/导入/重命名/删除/记事本编辑）、开机自启、关于 DeskMeter、退出。
/// 刷新间隔 / 点击穿透 / 显示器 由配置文件驱动（conky.config update_interval + deskmeter 块）。
/// </summary>
public sealed class TrayIcon : IDisposable
{
    private readonly NotifyIcon _icon;
    private readonly WidgetWindow _window;
    private string _configPath;
    private readonly ConfigManager _configs;
    private readonly ToolStripMenuItem _configMenu;
    private readonly ToolStripMenuItem _autostartItem;

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

        _configMenu = new ToolStripMenuItem("配置▶");

        _autostartItem = new ToolStripMenuItem("开机自启")
        {
            Checked = Autostart.IsEnabled(),
        };
        _autostartItem.Click += (_, _) => ToggleAutostart();

        var about = new ToolStripMenuItem("关于 DeskMeter");
        about.Click += (_, _) => ShowAbout();

        var exit = new ToolStripMenuItem("退出");
        exit.Click += (_, _) => _window.Close();

        var menu = new ContextMenuStrip();
        menu.Items.AddRange(new ToolStripItem[]
        {
            _configMenu, _autostartItem, about,
            new ToolStripSeparator(),
            exit,
        });
        // 每次弹出菜单：刷新开机自启勾选 + 重建配置子菜单（勾选当前配置）
        menu.Opening += (_, _) =>
        {
            _autostartItem.Checked = Autostart.IsEnabled();
            RebuildConfigMenu();
        };
        _icon.ContextMenuStrip = menu;
    }

    /// <summary>重建"配置▶"子菜单：切换/导入/重命名/删除/记事本编辑。</summary>
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
        _configMenu.DropDownItems.Add("导入配置…", null, (_, _) => ImportConfig());
        _configMenu.DropDownItems.Add("重命名…", null, (_, _) => RenameConfig());
        _configMenu.DropDownItems.Add("删除", null, (_, _) => DeleteConfig());
        _configMenu.DropDownItems.Add(new ToolStripSeparator());
        _configMenu.DropDownItems.Add("用记事本编辑当前配置", null, (_, _) => OpenConfigEditor());
    }

    private void SwitchTo(ConfigEntry entry)
    {
        if (_configs.SetCurrent(entry))
        {
            _configPath = entry.Path;
            _window.SwitchConfig(entry.Path);
        }
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
            var defaultName = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
            var name = InputDialog.Ask(null, "导入配置", "配置名称：", defaultName);
            if (string.IsNullOrWhiteSpace(name)) name = defaultName;
            var entry = _configs.Import(dialog.FileName, name);
            if (entry is null)
            {
                MessageBox.Show("导入失败", "DeskMeter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            SwitchTo(entry);
        }
        catch { }
    }

    private void RenameConfig()
    {
        var current = _configs.Current();
        if (current is null) return;
        var name = InputDialog.Ask(null, "重命名配置", "新名称：", current.Name);
        if (string.IsNullOrWhiteSpace(name) || name == current.Name) return;
        if (_configs.Rename(current, name))
        {
            _configPath = System.IO.Path.Combine(_configs.ConfigsDirectory, name + ".conf");
            RebuildConfigMenu();
        }
        else
        {
            MessageBox.Show("重命名失败（名称可能已存在）", "DeskMeter", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }

    private void DeleteConfig()
    {
        var current = _configs.Current();
        if (current is null) return;
        if (MessageBox.Show($"删除配置「{current.Name}」？", "DeskMeter",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes) return;
        if (_configs.Delete(current))
        {
            // 删除当前配置后切到剩余的第一个配置（或留在原窗口内容）
            var next = _configs.List().FirstOrDefault();
            if (next is not null) SwitchTo(next);
        }
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
        catch { }
    }

    private void ToggleAutostart()
    {
        var ok = Autostart.SetEnabled(!_autostartItem.Checked);
        if (ok) _autostartItem.Checked = !_autostartItem.Checked;
    }

    private static void ShowAbout()
    {
        MessageBox.Show("DeskMeter " + DeskMeter.Core.Objects.VariableEvaluator.Version.Replace("DeskMeter ", "v") +
            "\nConky 风格的 Windows 桌面系统监控小部件。\nMIT License · 开源免费，无广告、无遥测。\n" +
            "项目主页: github.com/AllenXiu/DeskMeter", "关于 DeskMeter", MessageBoxButtons.OK, MessageBoxIcon.Information);
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