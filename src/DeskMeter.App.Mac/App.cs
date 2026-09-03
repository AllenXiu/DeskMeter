using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Themes.Fluent;

namespace DeskMeter.App.Mac;

public sealed class App : Application
{
    public override void Initialize()
    {
        Styles.Add(new FluentTheme());
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            var opts = CliOptions.Parse(Environment.GetCommandLineArgs().Skip(1).ToArray());
            if (opts.ConsoleDump)
            {
                ConsoleDump.Run(opts);
                desktop.Shutdown();
                return;
            }
            var window = new MacWidgetWindow(opts);
            desktop.MainWindow = window;
            window.Show();
            // Avalonia 启动完成后再强制 accessory（无 Dock 图标）；1 秒后再兜底一次
            MacWindowHacks.HideFromDock();
            var dockTimer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(1) };
            dockTimer.Tick += (_, _) => { dockTimer.Stop(); MacWindowHacks.HideFromDock(); };
            dockTimer.Start();
            if (opts.SnapshotPath is not null)
            {
                var snap = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(2.2) };
                snap.Tick += (_, _) =>
                {
                    snap.Stop();
                    window.SaveSnapshot(opts.SnapshotPath);
                    window.Close();
                };
                snap.Start();
            }
            else if (opts.SmokeSeconds > 0)
            {
                var timer = new Avalonia.Threading.DispatcherTimer { Interval = TimeSpan.FromSeconds(opts.SmokeSeconds) };
                timer.Tick += (_, _) => { timer.Stop(); window.Close(); };
                timer.Start();
            }
        }
        base.OnFrameworkInitializationCompleted();
    }
}
