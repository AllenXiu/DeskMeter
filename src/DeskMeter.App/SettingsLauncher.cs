using DeskMeter.Core.Config;

namespace DeskMeter.App;

/// <summary>设置窗口单实例启动器（托盘 / 鼠标事件共用）。</summary>
public static class SettingsLauncher
{
    private static SettingsWindow? _instance;
    private static ConfigManager? _configs;

    public static void Open(string configPath) => Open(configPath, new ConfigManager());

    public static void Open(string configPath, ConfigManager configs)
    {
        _configs = configs;
        if (_instance is { IsLoaded: true })
        {
            _instance.Activate();
            return;
        }
        _instance = new SettingsWindow(configPath, configs);
        _instance.Closed += (_, _) => _instance = null;
        _instance.Show();
    }
}
