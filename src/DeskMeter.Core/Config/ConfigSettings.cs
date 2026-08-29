using MoonSharp.Interpreter;

namespace DeskMeter.Core.Config;

/// <summary>
/// 配置注册表：保存 conky.config 表中的原始值，并提供类型化读取。
/// 未知键被忽略（FR-CFG-1），不报错。
/// </summary>
public sealed class ConfigSettings
{
    private readonly Dictionary<string, object?> _values;

    public ConfigSettings(string sourcePath, IReadOnlyDictionary<string, object?> values)
    {
        SourcePath = sourcePath;
        _values = new Dictionary<string, object?>(values, StringComparer.OrdinalIgnoreCase);
    }

    public string SourcePath { get; }

    public IReadOnlyDictionary<string, object?> Values => _values;

    public bool TryGetRaw(string key, out object? value) => _values.TryGetValue(key, out value);

    public string? GetString(string key)
    {
        return _values.TryGetValue(key, out var v) && v is string s ? s : null;
    }

    public double GetNumber(string key, double fallback = 0)
    {
        if (_values.TryGetValue(key, out var v))
        {
            if (v is double d) return d;
            if (v is long l) return l;
            if (v is int i) return i;
            if (v is string s && double.TryParse(s, System.Globalization.NumberStyles.Any,
                    System.Globalization.CultureInfo.InvariantCulture, out var parsed)) return parsed;
        }
        return fallback;
    }

    public bool GetBool(string key, bool fallback = false)
    {
        if (_values.TryGetValue(key, out var v))
        {
            if (v is bool b) return b;
            if (v is string s) return s.Trim().ToLowerInvariant() is "true" or "yes" or "1";
            if (v is double d) return d != 0;
            if (v is long l) return l != 0;
        }
        return fallback;
    }

    public IReadOnlyList<string> GetStringList(string key)
    {
        if (_values.TryGetValue(key, out var v) && v is IEnumerable<string> list) return list.ToList();
        return Array.Empty<string>();
    }

    public WidgetAlignment GetAlignment(string key = "alignment") =>
        WidgetAlignmentParser.Parse(GetString(key));

    /// <summary>默认文字颜色（default_color），hex 或命名颜色，解析失败回退白色。</summary>
    public string GetDefaultColor() => GetString("default_color") ?? "FFFFFF";

    public double GetUpdateInterval(double fallback = 2.0)
    {
        var v = GetNumber("update_interval", fallback);
        return v > 0 ? v : fallback;
    }
}
