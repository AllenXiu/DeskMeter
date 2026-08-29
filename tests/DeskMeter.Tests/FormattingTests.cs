using DeskMeter.Core.Objects;
using DeskMeter.Core.Text;
using Xunit;

namespace DeskMeter.Tests;

public class FormattingTests
{
    [Theory]
    [InlineData(512, "512B")]
    [InlineData(4.2 * 1024, "4.2KiB")]
    [InlineData(120 * 1024, "120KiB")]
    [InlineData(2.4 * 1024 * 1024, "2.4MiB")]
    [InlineData(4.2 * 1024 * 1024 * 1024, "4.2GiB")]
    [InlineData(16L * 1024L * 1024L * 1024L, "16GiB")]
    public void HumanBytes_Formats(double bytes, string expected)
    {
        Assert.Equal(expected, HumanBytes.Format(bytes));
    }

    [Theory]
    [InlineData(30, "30s")]
    [InlineData(60 + 12, "1m")]
    [InlineData(3600 + 720, "1h 12m")]
    [InlineData(3 * 86400 + 4 * 3600 + 12 * 60, "3d 4h 12m")]
    public void HumanTime_Formats(double seconds, string expected)
    {
        Assert.Equal(expected, HumanTime.FormatUptime(TimeSpan.FromSeconds(seconds)));
    }

    [Fact]
    public void Strftime_Subset()
    {
        var dt = new DateTime(2025, 1, 1, 14, 35, 30);
        Assert.Equal("14:35", Strftime.Format("%H:%M", dt));
        Assert.Equal("2025-01-01", Strftime.Format("%Y-%m-%d", dt));
        Assert.Equal("Wed", Strftime.Format("%a", dt));
    }

    [Fact]
    public void ColorParser_Hex_Named_Palette()
    {
        Assert.Equal(new WidgetBrush(0x88, 0xCC, 0xFF), ColorParser.Parse("#88CCFF"));
        Assert.Equal(new WidgetBrush(0xBE, 0xBE, 0xBE), ColorParser.Parse("grey")); // X11/Conky grey = 190
        Assert.Equal(new WidgetBrush(0x88, 0xCC, 0xFF), ColorParser.Parse("0", TestHelpers.Settings()));
        Assert.Equal(new WidgetBrush(0xFF, 0xFF, 0xFF), ColorParser.Parse("", TestHelpers.Settings()));
    }

    [Fact]
    public void NormalizeDiskPath_Root()
    {
        var root = System.IO.Path.GetPathRoot(Environment.SystemDirectory);
        Assert.Equal(root, Core.Data.SystemSnapshot.NormalizeDiskPath("/"));
        Assert.Equal("C:\\", Core.Data.SystemSnapshot.NormalizeDiskPath("C:"));
        Assert.Equal("C:\\", Core.Data.SystemSnapshot.NormalizeDiskPath("c:/"));
    }
}
