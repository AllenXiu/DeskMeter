using System.Globalization;
using System.Text;

namespace DeskMeter.Core.Text;

/// <summary>strftime 子集格式化（${time %H:%M} / ${date %Y-%m-%d}，FR-VAR-3）。</summary>
public static class Strftime
{
    public static string Format(string format, DateTime dt)
    {
        if (string.IsNullOrEmpty(format)) return string.Empty;
        var sb = new StringBuilder();
        for (var i = 0; i < format.Length; i++)
        {
            var c = format[i];
            if (c != '%' || i + 1 >= format.Length) { sb.Append(c); continue; }
            var code = format[++i];
            switch (code)
            {
                case '%': sb.Append('%'); break;
                case 'a': sb.Append(dt.ToString("ddd", CultureInfo.InvariantCulture)); break;
                case 'A': sb.Append(dt.ToString("dddd", CultureInfo.InvariantCulture)); break;
                case 'b': sb.Append(dt.ToString("MMM", CultureInfo.InvariantCulture)); break;
                case 'B': sb.Append(dt.ToString("MMMM", CultureInfo.InvariantCulture)); break;
                case 'c': sb.Append(dt.ToString("ddd MMM d HH:mm:ss yyyy", CultureInfo.InvariantCulture)); break;
                case 'd': sb.Append(dt.ToString("dd", CultureInfo.InvariantCulture)); break;
                case 'H': sb.Append(dt.ToString("HH", CultureInfo.InvariantCulture)); break;
                case 'I': sb.Append(dt.ToString("hh", CultureInfo.InvariantCulture)); break;
                case 'j': sb.Append(dt.DayOfYear.ToString("000", CultureInfo.InvariantCulture)); break;
                case 'm': sb.Append(dt.ToString("MM", CultureInfo.InvariantCulture)); break;
                case 'M': sb.Append(dt.ToString("mm", CultureInfo.InvariantCulture)); break;
                case 'p': sb.Append(dt.ToString("tt", CultureInfo.InvariantCulture)); break;
                case 'S': sb.Append(dt.ToString("ss", CultureInfo.InvariantCulture)); break;
                case 's': sb.Append(new DateTimeOffset(dt).ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)); break;
                case 'U': sb.Append(CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(dt, CalendarWeekRule.FirstFullWeek, DayOfWeek.Sunday).ToString("00", CultureInfo.InvariantCulture)); break;
                case 'w': sb.Append(((int)dt.DayOfWeek).ToString(CultureInfo.InvariantCulture)); break;
                case 'W': sb.Append(CultureInfo.InvariantCulture.Calendar.GetWeekOfYear(dt, CalendarWeekRule.FirstFullWeek, DayOfWeek.Monday).ToString("00", CultureInfo.InvariantCulture)); break;
                case 'x': sb.Append(dt.ToString("MM/dd/yy", CultureInfo.InvariantCulture)); break;
                case 'X': sb.Append(dt.ToString("HH:mm:ss", CultureInfo.InvariantCulture)); break;
                case 'y': sb.Append(dt.ToString("yy", CultureInfo.InvariantCulture)); break;
                case 'Y': sb.Append(dt.ToString("yyyy", CultureInfo.InvariantCulture)); break;
                case 'z': sb.Append(dt.ToString("zzz", CultureInfo.InvariantCulture)); break;
                case 'Z': sb.Append(TimeZoneInfo.Local.StandardName); break;
                default: sb.Append('%').Append(code); break;
            }
        }
        return sb.ToString();
    }
}
