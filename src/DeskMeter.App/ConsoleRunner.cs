using System.IO;
using DeskMeter.Core.Config;
using DeskMeter.Core.Data;
using DeskMeter.Core.Objects;

namespace DeskMeter.App;

/// <summary>
/// Console 渲染后端（≈ Display Output 抽象中的 Console）：加载配置 → 采集一次数据 →
/// 执行 Object Tree → 输出纯文本。用于无头验证与 CI。
/// </summary>
public static class ConsoleRunner
{
    public static int Run(CliOptions options)
    {
        try
        {
            var engine = new LuaConfigEngine();
            var config = engine.LoadFile(options.ConfigPath);
            var registry = new ObjectRegistry();
            var nodes = ConkyTextParser.Parse(config.Text, registry, config.Settings);

            using var collector = new SystemDataCollector(enableTemperature: false);
            if (config.Settings.GetBool("temperature", true) &&
                (config.Text.Contains("platform", StringComparison.OrdinalIgnoreCase) || config.Text.Contains("hddtemp", StringComparison.OrdinalIgnoreCase)))
                collector.RequestTemperature();
            (collector.CollectDiskMetrics, collector.CollectGpuMetrics, collector.CollectNetMetrics) = config.Settings.GetTopMetricsNeeded();
            var data = collector.Collect();

            var layout = new WidgetLayout();
            var ctx = new RenderContext(data, config.Settings, layout) { LuaScript = config.LuaScript };
            foreach (var node in nodes) node.Print(ctx);

            Console.Out.WriteLine("---- " + Path.GetFileName(options.ConfigPath) + " ----");
            Console.Out.Write(layout.ToConsoleText());
            return 0;
        }
        catch (ConkyConfigException ex)
        {
            Console.Error.WriteLine("配置错误: " + ex.Message);
            return 2;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("运行错误: " + ex);
            return 1;
        }
    }
}
