using System.Globalization;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Navigation;
using DeskMeter.Core.Config;
using DeskMeter.Core.Objects;

namespace DeskMeter.App;

/// <summary>
/// 设置窗口（FR-SET）：三页 = 常规（刷新间隔/点击穿透/开机自启/显示器）+
/// 配置（conky.conf 编辑器，保存写回 + 热重载）+ 关于。
/// </summary>
public partial class SettingsWindow : Window
{
    private string _configPath;
    private readonly ConfigManager _configs;
    private readonly ConfigSettings _settings;
    private ConfigEntry? _selected;

    public SettingsWindow(string configPath) : this(configPath, new ConfigManager()) { }

    public SettingsWindow(string configPath, ConfigManager configs)
    {
        InitializeComponent();
        _configPath = configPath;
        _configs = configs;

        ConfigSettings? settings = null;
        try
        {
            settings = new LuaConfigEngine().LoadFile(configPath).Settings;
        }
        catch
        {
            // 配置非法时仍允许打开设置窗口（常规项照常可保存）
        }
        _settings = settings ?? new ConfigSettings(configPath, new Dictionary<string, object?>());

        IntervalBox.Text = _settings.GetUpdateInterval(2.0).ToString("0.##", CultureInfo.InvariantCulture);
        ClickThroughBox.IsChecked = _settings.GetBool("click_through", true);
        AutostartBox.IsChecked = Autostart.IsEnabled();

        var screens = System.Windows.Forms.Screen.AllScreens;
        MonitorBox.ItemsSource = screens.Select((s, i) => $"{i}: {s.DeviceName.TrimEnd('\\')}").ToList();
        var monitor = (int)_settings.GetNumber("monitor", 0);
        MonitorBox.SelectedIndex = screens.Length == 0 ? -1 : Math.Clamp(monitor, 0, screens.Length - 1);

        var full = Path.GetFullPath(configPath);
        ConfigPathText.Text = full;
        AboutConfigPath.Text = full;
        EditorConfigPath.Text = full;
        AboutVersionText.Text = "版本 " + VariableEvaluator.Version.Replace("DeskMeter ", "v");

        RefreshConfigList();
    }

    private void RefreshConfigList()
    {
        var current = _configs.Current()?.Name;
        var selected = _selected?.Name;
        ConfigList.Items.Clear();
        foreach (var entry in _configs.List())
        {
            var item = new ListBoxItem
            {
                Content = entry.Name,
                Tag = entry,
                FontWeight = string.Equals(entry.Name, current, StringComparison.OrdinalIgnoreCase)
                    ? FontWeights.Bold : FontWeights.Normal,
            };
            ConfigList.Items.Add(item);
        }
        if (selected is not null)
        {
            foreach (var item in ConfigList.Items.OfType<ListBoxItem>())
                if (string.Equals(item.Tag?.ToString(), selected, StringComparison.OrdinalIgnoreCase))
                    ConfigList.SelectedItem = item;
        }
        else if (ConfigList.Items.Count > 0)
        {
            ConfigList.SelectedIndex = 0;
        }
    }

    private void OnConfigSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
    {
        _selected = (ConfigList.SelectedItem as System.Windows.Controls.ListBoxItem)?.Tag as ConfigEntry;
    }

    private void OnConfigDoubleClick(object sender, System.Windows.Input.MouseButtonEventArgs e)
    {
        if (_selected is not null) SwitchConfig(_selected);
    }

    private void OnSwitchConfig(object sender, RoutedEventArgs e)
    {
        if (_selected is not null) SwitchConfig(_selected);
    }

    private void SwitchConfig(ConfigEntry entry)
    {
        if (_configs.SetCurrent(entry))
        {
            _configPath = entry.Path;
            EditorConfigPath.Text = entry.Path;
            RefreshConfigList();
        }
    }

    private void OnImportConfig(object sender, RoutedEventArgs e)
    {
        try
        {
            var dialog = new Microsoft.Win32.OpenFileDialog
            {
                Title = "导入 DeskMeter 配置",
                Filter = "Conky 配置 (*.conf)|*.conf|所有文件 (*.*)|*.*",
            };
            if (dialog.ShowDialog(this) != true) return;
            var defaultName = System.IO.Path.GetFileNameWithoutExtension(dialog.FileName);
            var name = InputDialog.Ask(this, "导入配置", "配置名称：", defaultName);
            if (string.IsNullOrWhiteSpace(name)) name = defaultName;
            var entry = _configs.Import(dialog.FileName, name);
            if (entry is null)
            {
                MessageBox.Show(this, "导入失败", "DeskMeter", MessageBoxButton.OK, MessageBoxImage.Warning);
                return;
            }
            SwitchConfig(entry);
        }
        catch { }
    }

    private void OnRenameConfig(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        var name = InputDialog.Ask(this, "重命名配置", "新名称：", _selected.Name);
        if (string.IsNullOrWhiteSpace(name) || name == _selected.Name) return;
        if (_configs.Rename(_selected, name))
            RefreshConfigList();
        else
            MessageBox.Show(this, "重命名失败（名称可能已存在）", "DeskMeter", MessageBoxButton.OK, MessageBoxImage.Warning);
    }

    private void OnDeleteConfig(object sender, RoutedEventArgs e)
    {
        if (_selected is null) return;
        if (MessageBox.Show(this, $"删除配置「{_selected.Name}」？", "DeskMeter",
                MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        if (_configs.Delete(_selected))
        {
            _selected = null;
            RefreshConfigList();
        }
    }

    private void OnOpenExternalEditor(object sender, RoutedEventArgs e)
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
            // 无法打开时忽略
        }
    }

    /// <summary>与托盘联动：每次激活窗口时以注册表为准刷新开机自启状态。</summary>
    protected override void OnActivated(EventArgs e)
    {
        base.OnActivated(e);
        AutostartBox.IsChecked = Autostart.IsEnabled();
    }

    private void OnSave(object sender, RoutedEventArgs e)
    {
        if (!double.TryParse(IntervalBox.Text, NumberStyles.Any, CultureInfo.InvariantCulture, out var interval) ||
            interval <= 0)
        {
            MessageBox.Show(this, "刷新间隔必须是大于 0 的数字", "输入无效",
                MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var monitor = MonitorBox.SelectedIndex >= 0 ? MonitorBox.SelectedIndex : 0;
        var clickThrough = ClickThroughBox.IsChecked == true;
        // 配置内容以磁盘为准（用户用系统编辑器维护）；仅写回常规项
        var current = File.ReadAllText(_configPath);
        var content = ConfigWriteBack.Update(current, interval, clickThrough, monitor);

        try
        {
            File.WriteAllText(_configPath, content);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "保存失败：" + ex.Message, "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        Autostart.SetEnabled(AutostartBox.IsChecked == true);
        Close(); // 热重载由 FileSystemWatcher 在 1s 内自动完成（FR-SET-1）
    }

    private void OnCancel(object sender, RoutedEventArgs e) => Close();

    private void OnRestoreDefault(object sender, RoutedEventArgs e)
    {
        // 恢复默认：把内置默认配置写回文件并热重载（不依赖编辑器）
        var path = Path.Combine(AppContext.BaseDirectory, "samples", "conky.conf");
        var content = File.Exists(path) ? File.ReadAllText(path) : DefaultConfig;
        try
        {
            File.WriteAllText(_configPath, content);
        }
        catch (Exception ex)
        {
            MessageBox.Show(this, "恢复默认失败：" + ex.Message, "错误",
                MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnNavigate(object sender, RequestNavigateEventArgs e)
    {
        try
        {
            System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
            {
                FileName = e.Uri.ToString(),
                UseShellExecute = true,
            });
        }
        catch { }
        e.Handled = true;
    }

    private const string DefaultConfig = """
        -- DeskMeter 默认配置
        conky.config = {
            update_interval = 2,
            alignment = 'top_right',
            gap_x = 16,
            gap_y = 16,
            font = 'Consolas:size=12',
            default_color = 'FFFFFF',
            color0 = '88CCFF',
            minimum_width = 200,
            maximum_width = 200,
        };
        conky.text = [[
        $color0$hostname$color  ${time %H:%M}
        $hr
        CPU  $cpu%  ${cpubar 6}
        内存  $memperc%  ${membar 6}
        磁盘  ${fs_free_perc /}%  ${fs_bar 6 /}
        网络  ↓ $downspeed  ↑ $upspeed
        运行时间  $uptime
        ]];
        """;
}
