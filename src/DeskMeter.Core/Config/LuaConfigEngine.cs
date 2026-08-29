using MoonSharp.Interpreter;

namespace DeskMeter.Core.Config;

/// <summary>
/// 配置引擎（≈ Conky lua-config）：用 MoonSharp（纯 C# Lua 5.2）完整执行 conky.conf，
/// 然后读取全局表 conky.config / conky.text。支持注释、函数、变量、计算、dofile()。
/// </summary>
public sealed class LuaConfigEngine
{
    public ConkyConfig LoadFile(string path)
    {
        string source;
        try
        {
            source = File.ReadAllText(path);
        }
        catch (Exception ex)
        {
            throw new ConkyConfigException($"无法读取配置文件: {path} ({ex.Message})", ex);
        }

        return Parse(source, path);
    }

    public ConkyConfig Parse(string luaSource, string sourcePath = "")
    {
        Script script;
        try
        {
            script = new Script();
            script.Options.DebugPrint = _ => { };
            // 与 Conky 相同：执行前预注册全局表 conky（否则 conky.config = {...} 会 nil 索引）
            script.Globals["conky"] = DynValue.NewTable(script);
            script.DoString(luaSource);
        }
        catch (InterpreterException ex)
        {
            throw new ConkyConfigException(
                $"Lua 执行失败（行 {ex.DecoratedMessage}）：{ex.Message}", ex);
        }
        catch (Exception ex)
        {
            throw new ConkyConfigException($"Lua 执行失败: {ex.Message}", ex);
        }

        var conky = script.Globals.Get("conky");
        if (conky.IsNil() || conky.Type != DataType.Table)
        {
            throw new ConkyConfigException("配置缺少全局表 conky（应定义 conky.config 与 conky.text）");
        }

        var conkyTable = conky.Table;
        var settings = ParseSettings(conkyTable.Get("config"));
        var text = ParseText(conkyTable.Get("text"));

        return new ConkyConfig(sourcePath, settings, text, luaSource);
    }

    private static ConfigSettings ParseSettings(DynValue config)
    {
        var values = new Dictionary<string, object?>(StringComparer.OrdinalIgnoreCase);
        if (config.IsNil() || config.Type != DataType.Table) return new ConfigSettings("", values);

        foreach (var pair in config.Table.Pairs)
        {
            var key = pair.Key.CastToString();
            if (string.IsNullOrEmpty(key)) continue;
            values[key] = Coerce(pair.Value);
        }
        return new ConfigSettings("", values);
    }

    private static string ParseText(DynValue text)
    {
        if (text.IsNil()) return string.Empty;
        var s = text.CastToString();
        return s ?? string.Empty;
    }

    private static object? Coerce(DynValue value)
    {
        switch (value.Type)
        {
            case DataType.Nil: return null;
            case DataType.String: return value.String;
            case DataType.Number: return value.Number;
            case DataType.Boolean: return value.Boolean;
            case DataType.Table:
                // 表只按字符串数组处理（如 own_window_hints 的逗号串已由 Lua 侧保证）；
                // 若为纯字符串序列则转为数组。
                var list = new List<string>();
                bool allStrings = true;
                foreach (var pair in value.Table.Pairs)
                {
                    var item = pair.Value.CastToString();
                    if (item is null) { allStrings = false; break; }
                    list.Add(item);
                }
                return allStrings ? (object?)list : null;
            default:
                return value.ToObject();
        }
    }
}
