namespace DeskMeter.App.Mac;

public static class ConfigPathResolver
{
    public static string? Resolve(string? explicitPath)
    {
        if (!string.IsNullOrEmpty(explicitPath) && System.IO.File.Exists(explicitPath)) return explicitPath;
        var candidates = new[]
        {
            System.IO.Path.Combine(AppContext.BaseDirectory, "samples", "conky.mac.conf"),
            System.IO.Path.Combine(Environment.CurrentDirectory, "samples", "conky.mac.conf"),
            System.IO.Path.Combine(AppContext.BaseDirectory, "samples", "conky.conf"),
            System.IO.Path.Combine(Environment.CurrentDirectory, "samples", "conky.conf"),
        };
        return candidates.FirstOrDefault(System.IO.File.Exists);
    }

    public static string WriteFallback()
    {
        var path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), "deskmeter-fallback.conf");
        var lines = new[]
        {
            "conky.config = {",
            "    update_interval = 2, alignment = 'top_right',",
            "    own_window = true, own_window_transparent = true,",
            "    font = 'Menlo:size=12', default_color = 'FFFFFF',",
            "    color0 = '88CCFF', gap_x = 16, gap_y = 16,",
            "};",
            "conky.text = [[",
            "${color0}${hostname}${color}  ${time %H:%M:%S}",
            "$hr",
            "CPU  $cpu%  $cpubar 6,120",
            "Mem  $memperc%  $membar 6,120",
            "Disk $fs_free_perc /%  $fs_bar 6,120 /",
            "Net  down $downspeed  up $upspeed",
            "Top  ${top name 1} ${top cpu 1}%",
            "]];",
            "deskmeter = { click_through = true };",
        };
        System.IO.File.WriteAllLines(path, lines);
        return path;
    }
}
