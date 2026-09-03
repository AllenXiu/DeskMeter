using DeskMeter.Core.Config;
using DeskMeter.Core.Data;
using DeskMeter.Core.Objects;

namespace DeskMeter.App.Mac;

public static class ConsoleDump
{
    public static void Run(CliOptions opts)
    {
        try
        {
            var path = ConfigPathResolver.Resolve(opts.ConfigPath) ?? ConfigPathResolver.WriteFallback();
            var engine = new LuaConfigEngine();
            var config = engine.LoadFile(path);
            var registry = new ObjectRegistry();
            var nodes = ConkyTextParser.Parse(config.Text, registry, config.Settings);
            using var collector = new MacSystemDataCollector();
            collector.Collect(); // 预热（CPU 差分基线）
            System.Threading.Thread.Sleep(1100);
            var data = collector.Collect();
            var layout = new WidgetLayout();
            var ctx = new RenderContext(data, config.Settings, layout) { UpdateNumber = 1 };
            foreach (var node in nodes) node.Print(ctx);
            Console.WriteLine("---- " + System.IO.Path.GetFileName(path) + " ----");
            Console.Write(layout.ToConsoleText());
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine("dump error: " + ex);
        }
    }
}
