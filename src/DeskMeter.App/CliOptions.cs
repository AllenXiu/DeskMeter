using System.IO;

namespace DeskMeter.App;

/// <summary>命令行参数：--config &lt;path&gt;、--backend wpf|console、--help。</summary>
public sealed class CliOptions
{
    public string ConfigPath { get; private set; } = DefaultConfigPath();

    /// <summary>是否显式传入了 --config（否则用配置库当前配置）。</summary>
    public bool HasExplicitConfig { get; private set; }

    /// <summary>wpf = 透明桌面小部件；console = 一次性渲染到 stdout（无头验证用）。</summary>
    public string Backend { get; private set; } = "wpf";

    /// <summary>wpf 模式下创建窗口并自动关闭（CI/冒烟验证）。</summary>
    public bool SmokeTest { get; private set; }

    /// <summary>内存诊断：启动后每 10s 打印 GC/工作集/模块统计，60s 后自动退出（NFR-2 监测用）。</summary>
    public bool MemInfo { get; private set; }

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--config" when i + 1 < args.Length:
                    o.ConfigPath = Path.GetFullPath(args[++i]);
                    o.HasExplicitConfig = true;
                    break;
                case "--backend" when i + 1 < args.Length:
                    o.Backend = args[++i].ToLowerInvariant();
                    break;
                case "--smoke-test":
                    o.SmokeTest = true;
                    break;
                case "--mem-info":
                    o.MemInfo = true;
                    break;
                case "--help":
                case "-h":
                    Console.WriteLine(HelpText);
                    Environment.Exit(0);
                    break;
            }
        }
        if (o.Backend is not ("wpf" or "console"))
        {
            Console.Error.WriteLine($"未知后端: {o.Backend}（支持 wpf / console）");
            Environment.Exit(2);
        }
        return o;
    }

    private static string DefaultConfigPath() =>
        Path.Combine(AppContext.BaseDirectory, "samples", "conky.conf");

    private const string HelpText = """
        DeskMeter - Conky 风格 Windows 桌面系统监控小部件 (P0)

        用法: DeskMeter [选项]

        --config <path>   配置文件路径（默认: samples/conky.conf）
        --backend <name>  wpf（默认，透明桌面小部件）| console（渲染一次到 stdout）
        --smoke-test       wpf 模式：创建窗口后 2.5s 自动关闭（冒烟验证）
        --help            显示帮助
        """;
}
