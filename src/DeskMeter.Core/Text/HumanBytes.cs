using System.Globalization;

namespace DeskMeter.Core.Text;

/// <summary>字节数人类可读格式化（4.2GiB / 120KiB / 512B）。</summary>
public static class HumanBytes
{
    public static string Format(double bytes)
    {
        if (bytes < 0) bytes = 0;
        double v = bytes;
        string unit;
        if (v < 1024) { unit = "B"; }
        else if (v < 1024 * 1024) { v /= 1024; unit = "KiB"; }
        else if (v < 1024L * 1024 * 1024) { v /= 1024 * 1024; unit = "MiB"; }
        else { v /= 1024.0 * 1024 * 1024; unit = "GiB"; }
        return v.ToString("0.##", CultureInfo.InvariantCulture) + unit;
    }
}

/// <summary>运行时间格式化（3d 4h 12m / 4h 12m / 12m / 30s）。</summary>
public static class HumanTime
{
    public static string FormatUptime(TimeSpan t)
    {
        if (t.TotalSeconds < 60) return $"{(int)t.TotalSeconds}s";
        var parts = new List<string>();
        if (t.TotalDays >= 1) parts.Add($"{(int)t.TotalDays}d");
        if (t.Hours > 0 || parts.Count > 0) parts.Add($"{t.Hours}h");
        if (t.Minutes > 0 || parts.Count > 0) parts.Add($"{t.Minutes}m");
        return string.Join(" ", parts);
    }
}
