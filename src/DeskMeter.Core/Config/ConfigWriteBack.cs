using System.Globalization;
using System.Text.RegularExpressions;

namespace DeskMeter.Core.Config;

/// <summary>
/// 配置写回（FR-SET-1）：修改 conky.conf 的 update_interval 与 deskmeter 扩展块键
/// （click_through / monitor），仅做定向文本替换，不重排用户手写内容。
/// </summary>
public static class ConfigWriteBack
{
    private static readonly Regex UpdateIntervalRe = new(
        @"(?m)^(?<indent>\s*)update_interval\s*=\s*[0-9.]+\s*(?<tail>,)?\s*$",
        RegexOptions.CultureInvariant);

    private static readonly Regex ConfigOpenRe = new(
        @"conky\.config\s*=\s*\{", RegexOptions.CultureInvariant);

    private static readonly Regex DeskmeterBlockRe = new(
        @"deskmeter\s*=\s*\{(?<body>[^}]*)\}", RegexOptions.CultureInvariant);

    private static readonly Regex TextRe = new(
        @"(?m)^conky\.text\s*=", RegexOptions.CultureInvariant);

    /// <summary>按给定值更新配置文本；null 表示不修改对应项。</summary>
    public static string Update(string source, double? updateInterval, bool? clickThrough, int? monitor)
    {
        var s = source ?? string.Empty;
        if (updateInterval is { } ui) s = SetUpdateInterval(s, ui);
        if (clickThrough is { } ct) s = SetDeskmeterValue(s, "click_through", ct ? "true" : "false");
        if (monitor is { } m) s = SetDeskmeterValue(s, "monitor", m.ToString(CultureInfo.InvariantCulture));
        return s;
    }

    public static string SetUpdateInterval(string source, double seconds)
    {
        var value = seconds.ToString("0.##", CultureInfo.InvariantCulture);
        if (UpdateIntervalRe.IsMatch(source))
            return UpdateIntervalRe.Replace(source, "${indent}update_interval = " + value + "${tail}");
        // 配置表里没有 update_interval：在 conky.config = { 之后插入
        var m = ConfigOpenRe.Match(source);
        if (m.Success)
            return source.Insert(m.Index + m.Length, "\n    update_interval = " + value + ",");
        return source;
    }

    public static string SetDeskmeterValue(string source, string key, string value)
    {
        var block = DeskmeterBlockRe.Match(source);
        if (!block.Success)
        {
            // 没有 deskmeter 块：在 conky.text 定义之前插入（Lua 全局语句，位置不限）
            var text = TextRe.Match(source);
            var insert = "\ndeskmeter = { " + key + " = " + value + " };\n";
            if (text.Success) return source.Insert(text.Index, insert);
            return source + "\n" + insert;
        }

        var body = block.Groups["body"].Value;
        var keyRe = new Regex(@"\b" + Regex.Escape(key) + @"\s*=\s*[\w.]+", RegexOptions.CultureInvariant);
        if (keyRe.IsMatch(body))
        {
            var newBody = keyRe.Replace(body, key + " = " + value);
            return source.Replace(body, newBody);
        }

        // 块内没有该键：在块开头的 { 后插入
        var insertAt = block.Index + block.Groups[0].Value.IndexOf('{', StringComparison.Ordinal) + 1;
        return source.Insert(insertAt, " " + key + " = " + value + ",");
    }
}
