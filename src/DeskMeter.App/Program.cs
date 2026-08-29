using System.IO;
using System.Runtime.InteropServices;
using System.Windows;
using System.Windows.Threading;
using DeskMeter.Core.Config;

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

        // 多配置：--config 优先；否则用配置库当前配置（首次自动导入 samples 为"默认"）
        var configManager = new ConfigManager();
        var configPath = options.ConfigPath;
        if (!options.HasExplicitConfig)
        {
            var sample = System.IO.Path.Combine(AppContext.BaseDirectory, "samples", "conky.conf");
            configPath = configManager.EnsureDefault(sample)?.Path ?? configPath;
        }

        var window = new WidgetWindow(configPath);
        if (options.SmokeTest)
        {
            var timer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(2.5) };
            timer.Tick += (_, _) => { timer.Stop(); window.Close(); };
            timer.Start();
        }

        // --mem-info：NFR-2 内存诊断（每 10s 采样，60s 后退出）
        if (options.MemInfo)
        {
            var ticks = 0;
            var memTimer = new DispatcherTimer { Interval = TimeSpan.FromSeconds(10) };
            memTimer.Tick += (_, _) =>
            {
                ticks++;
                var proc = System.Diagnostics.Process.GetCurrentProcess();
                proc.Refresh();
                System.GC.Collect();
                System.GC.WaitForPendingFinalizers();
                System.GC.Collect();
                Console.WriteLine($"[mem {ticks * 10}s] workingSet={proc.WorkingSet64 / 1024 / 1024}MB " +
                    $"private={proc.PrivateMemorySize64 / 1024 / 1024}MB gcHeap={System.GC.GetTotalMemory(false) / 1024 / 1024}MB " +
                    $"serverGc={System.Runtime.GCSettings.IsServerGC} modules={proc.Modules.Count}");
                if (ticks >= 6) { memTimer.Stop(); window.Close(); }
            };
            memTimer.Start();
        }

        // 系统托盘（P2）：配置▶ / 设置… / 编辑配置… / 刷新 / 开机自启 / 退出
        using var tray = new TrayIcon(window, configPath, configManager);

        if (options.OpenSettings) SettingsLauncher.Open(configPath, configManager);

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
