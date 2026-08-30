namespace DeskMeter.Core.Config;

/// <summary>一次成功解析 conky.conf 的结果。</summary>
public sealed class ConkyConfig
{
    public ConkyConfig(string sourcePath, ConfigSettings settings, string text, string luaSource, MoonSharp.Interpreter.Script? luaScript = null)
    {
        SourcePath = sourcePath;
        Settings = settings;
        Text = text;
        LuaSource = luaSource;
        LuaScript = luaScript;
    }

    /// <summary>配置文件路径（内存解析时可为空串）。</summary>
    public string SourcePath { get; }

    public ConfigSettings Settings { get; }

    /// <summary>conky.text 段原文。</summary>
    public string Text { get; }

    /// <summary>完整 Lua 源码（用于错误定位）。</summary>
    public string LuaSource { get; }

    /// <summary>编译后的 MoonSharp 脚本（${lua 函数} 变量调用配置内定义函数用）。</summary>
    public MoonSharp.Interpreter.Script? LuaScript { get; }
}

/// <summary>配置解析失败信息（FR-CFG-3：失败时保留旧配置并提示错误位置）。</summary>
public sealed class ConkyConfigException : Exception
{
    public ConkyConfigException(string message, Exception? inner = null) : base(message, inner) { }
}
