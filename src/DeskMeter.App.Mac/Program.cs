using Avalonia;
using System.IO;

namespace DeskMeter.App.Mac;

internal static class Program
{
    [STAThread]
    public static void Main(string[] args)
    {
        // 单实例锁：重复启动直接退出（开机自启 + 手动启动并存时只保留一个）
        try
        {
            using var guard = new FileStream("/tmp/deskmeter.singleton",
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            DeskMeter.App.Mac.MacWindowHacks.HideFromDock(); // 无 Dock 图标（accessory 模式）
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        catch (IOException)
        {
            Console.WriteLine("DeskMeter 已在运行，本次启动退出。");
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("DeskMeter(mac) fatal: " + ex);
        }
    }

    public static AppBuilder BuildAvaloniaApp()
        => AppBuilder.Configure<App>().UsePlatformDetect().LogToTrace();
}
