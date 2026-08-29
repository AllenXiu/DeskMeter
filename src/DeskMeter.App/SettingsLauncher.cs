namespace DeskMeter.App;

/// <summary>设置窗口单实例启动器（托盘 / 鼠标事件共用）。</summary>
public static class SettingsLauncher
{
    private static SettingsWindow? _instance;

    public static void Open(string configPath)
    {
        if (_instance is { IsLoaded: true })
        {
            _instance.Activate();
            return;
        }
        _instance = new SettingsWindow(configPath);
        _instance.Closed += (_, _) => _instance = null;
        _instance.Show();
    }
}
