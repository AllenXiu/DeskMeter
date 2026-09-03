namespace DeskMeter.App.Mac;

public sealed record CliOptions
{
    public string? ConfigPath { get; init; }
    public bool ConsoleDump { get; init; }
    public string? SnapshotPath { get; init; }
    public int SmokeSeconds { get; init; }
    public bool? ClickThrough { get; init; }
    public string WindowLevel { get; init; } = string.Empty; // 空 = 由配置 deskmeter.pinned 决定

    public static CliOptions Parse(string[] args)
    {
        var o = new CliOptions();
        for (var i = 0; i < args.Length; i++)
        {
            switch (args[i])
            {
                case "--config" when i + 1 < args.Length: o = o with { ConfigPath = args[++i] }; break;
                case "--console-dump": o = o with { ConsoleDump = true }; break;
                case "--snapshot" when i + 1 < args.Length: o = o with { SnapshotPath = args[++i] }; break;
                case "--smoke" when i + 1 < args.Length && int.TryParse(args[i + 1], out var s): o = o with { SmokeSeconds = s }; i++; break;
                case "--click-through" when i + 1 < args.Length && bool.TryParse(args[i + 1], out var ct): o = o with { ClickThrough = ct }; i++; break;
                case "--window-level" when i + 1 < args.Length: o = o with { WindowLevel = args[++i] }; break;
            }
        }
        return o;
    }
}
