namespace DeskMeter.Core.Config;

/// <summary>配置库中的一个配置项。</summary>
public sealed record ConfigEntry(string Name, string Path);

/// <summary>
/// 多配置管理（P2）：配置库目录存放多个 conky.conf，支持导入（命名/冲突自动后缀）、
/// 重命名、删除、切换当前配置（.current 记录）。
/// </summary>
public sealed class ConfigManager
{
    private readonly string _configsDir;
    private readonly string _currentFile;

    public ConfigManager(string? baseDir = null)
    {
        _configsDir = baseDir ?? System.IO.Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "DeskMeter", "configs");
        _currentFile = System.IO.Path.Combine(_configsDir, ".current");
        try { Directory.CreateDirectory(_configsDir); } catch { }
    }

    public string ConfigsDirectory => _configsDir;

    /// <summary>按名称排序的配置列表。</summary>
    public IReadOnlyList<ConfigEntry> List()
    {
        try
        {
            return Directory.EnumerateFiles(_configsDir, "*.conf")
                .Select(p => new ConfigEntry(System.IO.Path.GetFileNameWithoutExtension(p), p))
                .OrderBy(e => e.Name, StringComparer.CurrentCultureIgnoreCase)
                .ToList();
        }
        catch
        {
            return Array.Empty<ConfigEntry>();
        }
    }

    /// <summary>当前配置；无则返回 null。</summary>
    public ConfigEntry? Current()
    {
        try
        {
            var name = System.IO.File.ReadAllText(_currentFile).Trim();
            return List().FirstOrDefault(e => e.Name == name);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>导入配置：复制到库并命名；重名时自动追加 (2)、(3)。</summary>
    public ConfigEntry? Import(string sourcePath, string? name = null)
    {
        try
        {
            if (!System.IO.File.Exists(sourcePath)) return null;
            var baseName = string.IsNullOrWhiteSpace(name)
                ? System.IO.Path.GetFileNameWithoutExtension(sourcePath)
                : name.Trim();
            if (baseName.Length == 0) baseName = "config";
            var target = System.IO.Path.Combine(_configsDir, baseName + ".conf");
            var n = 2;
            while (System.IO.File.Exists(target))
            {
                target = System.IO.Path.Combine(_configsDir, baseName + " (" + n + ").conf");
                n++;
            }
            System.IO.File.Copy(sourcePath, target);
            return new ConfigEntry(System.IO.Path.GetFileNameWithoutExtension(target), target);
        }
        catch
        {
            return null;
        }
    }

    /// <summary>重命名配置（改文件名；不修改 .current 指向的名称）。</summary>
    public bool Rename(ConfigEntry entry, string newName)
    {
        try
        {
            var name = newName.Trim();
            if (name.Length == 0) return false;
            var target = System.IO.Path.Combine(_configsDir, name + ".conf");
            if (System.IO.File.Exists(target) && !string.Equals(target, entry.Path, StringComparison.OrdinalIgnoreCase)) return false;
            System.IO.File.Move(entry.Path, target);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public bool Delete(ConfigEntry entry)
    {
        try
        {
            var wasCurrent = string.Equals(entry.Name, Current()?.Name, StringComparison.OrdinalIgnoreCase);
            System.IO.File.Delete(entry.Path);
            if (wasCurrent) ClearCurrent();
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>设为当前配置。</summary>
    public bool SetCurrent(ConfigEntry entry)
    {
        try
        {
            if (!System.IO.File.Exists(entry.Path)) return false;
            System.IO.File.WriteAllText(_currentFile, entry.Name);
            return true;
        }
        catch
        {
            return false;
        }
    }

    /// <summary>库为空时导入示例为"默认"并设为当前；返回当前配置。</summary>
    public ConfigEntry? EnsureDefault(string samplePath)
    {
        if (Current() is { } cur && System.IO.File.Exists(cur.Path)) return cur;
        if (List().Count == 0 && System.IO.File.Exists(samplePath))
        {
            var entry = Import(samplePath, "默认");
            if (entry is not null) SetCurrent(entry);
            return entry;
        }
        return Current();
    }

    private void ClearCurrent()
    {
        try { System.IO.File.Delete(_currentFile); } catch { }
    }
}