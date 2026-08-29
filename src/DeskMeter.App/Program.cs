using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;

namespace DeskMeter.App;

internal static class Program
{
    [STAThread]
    private static int Main(string[] args)
    {
        var options = CliOptions.Parse(args);

        if (options.Backend == "console")
        {
            AttachToParentConsole();
            return ConsoleRunner.Run(options);
        }

        var app = new Application
        {
            ShutdownMode = ShutdownMode.OnMainWindowClose
        };
        app.DispatcherUnhandledException += (_, e) =>
        {
            System.Diagnostics.Debug.WriteLine("DeskMeter fatal: " + e.Exception);
            e.Handled = true;
        };

        var window = new WidgetWindow(options.ConfigPath);
        if (options.SmokeTest)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            timer.Tick += (_, _) => { timer.Stop(); window.Close(); };
            timer.Start();
        }

        // 系统托盘（P2）：编辑配置… / 刷新 / 开机自启 / 退出
        using var tray = new TrayIcon(window, options.ConfigPath);

        if (options.OpenSettings) SettingsLauncher.Open(options.ConfigPath);

        app.Run(window);
        return 0;
    }

    /// <summary>WinExe 进程附加到父控制台，使 --backend console 的输出可见。</summary>
    private static void AttachToParentConsole()
    {
        try
        {
            AttachConsole(ATTACH_PARENT_PROCESS);
            var handle = GetStdHandle(STD_OUTPUT_HANDLE);
            if (handle != IntPtr.Zero && handle != INVALID_HANDLE)
            {
                var writer = new StreamWriter(new FileStream(
                    new Microsoft.Win32.SafeHandles.SafeFileHandle(handle, false), FileAccess.Write))
                { AutoFlush = true };
                Console.SetOut(writer);
                Console.SetError(writer);
            }
        }
        catch
        {
            // 无父控制台时静默降级
        }
    }

    private const uint ATTACH_PARENT_PROCESS = 0xFFFFFFFF;
    private const int STD_OUTPUT_HANDLE = -11;
    private static readonly IntPtr INVALID_HANDLE = new(-1);

    [DllImport("kernel32.dll")]
    private static extern bool AttachConsole(uint dwProcessId);

    [DllImport("kernel32.dll")]
    private static extern IntPtr GetStdHandle(int nStdHandle);
}
