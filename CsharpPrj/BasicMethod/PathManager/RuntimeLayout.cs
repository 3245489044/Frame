using System;
using System.IO;
using System.Reflection;
using System.Text.Json;
using System.Threading;

namespace PathManager;

/// <summary>
/// Runtime 发布布局单一解析入口（D3）。
/// <para>
/// Published 判据：环境变量 <c>FILEDRIVEN_RUNTIME_ROOT</c> 或从运行基目录向上找到同时含
/// <c>runtime.json</c> 与 <c>App\FileDriven.Launcher.exe</c> 的目录。Dev 模式（无标记）返回 false，
/// 调用方沿用既有仓库根探测。
/// </para>
/// <para>
/// 本类只依赖 BCL，且<b>不调用</b> <see cref="ProjectPathManager"/>（避免
/// <c>ProjectPathManager → RuntimeLayout → ProjectPathManager</c> 递归）。
/// </para>
/// </summary>
public static class RuntimeLayout
{
    public const string RuntimeRootEnvironmentVariable = "FILEDRIVEN_RUNTIME_ROOT";
    public const string DataRootEnvironmentVariable = "WF_DATA_ROOT";
    public const string LegacyDataRootEnvironmentVariable = "FILEDRIVEN_DATA_ROOT";
    public const string RuntimeMarkerFileName = "runtime.json";
    public const string AppDirectoryName = "App";
    public const string SharedDirectoryName = "Shared";
    public const string PluginsDirectoryName = "Plugins";
    public const string ConfigDirectoryName = "Config";
    public const string DataDirectoryName = "Data";
    public const string LogsRelativePath = "Data/Logs";
    public const string EntryRelativePath = "App/FileDriven.Launcher.exe";
    public const string CurrentPointerFileName = "current";

    private static readonly object SyncRoot = new();
    private static string? _publishedRoot;
    private static string? _dataRoot;
    private static string? _logsDir;
    private static bool _resolved;

    /// <summary>是否运行于分发包（Runtime 根已确认）。</summary>
    public static bool IsPublished => TryGetPublishedRoot() is not null;

    /// <summary>Published 根；Dev 模式返回 null。</summary>
    public static string? TryGetPublishedRoot()
    {
        if (_resolved)
            return _publishedRoot;

        lock (SyncRoot)
        {
            if (_resolved)
                return _publishedRoot;

            var fromEnvironment = NormalizeRuntimeRoot(
                Environment.GetEnvironmentVariable(RuntimeRootEnvironmentVariable));
            if (fromEnvironment is not null)
            {
                _publishedRoot = fromEnvironment;
                _resolved = true;
                return _publishedRoot;
            }

            foreach (var start in EnumerateStarts())
            {
                var found = FindUpward(start);
                if (found is not null)
                {
                    _publishedRoot = found;
                    _resolved = true;
                    return _publishedRoot;
                }
            }

            _resolved = true;
            return _publishedRoot;
        }
    }

    /// <summary>Runtime 根（Published 必得；Dev 调用即 fail-fast）。</summary>
    public static string Root => TryGetPublishedRoot()
        ?? throw new InvalidOperationException(
            "RuntimeLayout.Root 仅在 Published 模式可用；Dev 模式请使用 ProjectPathManager.FindProjectRoot()。");

    public static string AppDir => Path.Combine(Root, AppDirectoryName);
    public static string SharedDir => Path.Combine(Root, SharedDirectoryName);
    public static string PluginsDir => Path.Combine(Root, PluginsDirectoryName);
    public static string ConfigDir => Path.Combine(Root, ConfigDirectoryName);

    /// <summary>可写数据根：WF_DATA_ROOT / FILEDRIVEN_DATA_ROOT → runtime.json.data → Runtime\Data。</summary>
    public static string DataRoot => _dataRoot ??= ResolveDataRoot(Root);

    /// <summary>兼容旧名：等价于 <see cref="DataRoot"/>。</summary>
    public static string DataDir => DataRoot;

    /// <summary>日志目录：runtime.json.logs → DataRoot\Logs。</summary>
    public static string LogsDir => _logsDir ??= ResolveLogsDir(Root);

    public static string FrameworkConfigPath => Path.Combine(ConfigDir, "framework.config.json");

    /// <summary>解析路径 token；非 token 相对路径在 Published 下按 Runtime 根解析。</summary>
    public static string ResolveToken(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return value;

        var published = TryGetPublishedRoot();
        if (value[0] == '@')
        {
            if (published is null)
                return value;

            if (value.Equals("@runtime", StringComparison.OrdinalIgnoreCase)) return published;
            if (value.Equals("@app", StringComparison.OrdinalIgnoreCase)) return Path.Combine(published, AppDirectoryName);
            if (value.Equals("@shared", StringComparison.OrdinalIgnoreCase)) return Path.Combine(published, SharedDirectoryName);
            if (value.Equals("@plugins", StringComparison.OrdinalIgnoreCase)) return Path.Combine(published, PluginsDirectoryName);
            if (value.Equals("@config", StringComparison.OrdinalIgnoreCase)) return Path.Combine(published, ConfigDirectoryName);
            if (value.Equals("@data", StringComparison.OrdinalIgnoreCase)) return DataRoot;
            if (value.Equals("@logs", StringComparison.OrdinalIgnoreCase)) return LogsDir;

            if (value.StartsWith("@config/", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(published, ConfigDirectoryName,
                    value.Substring("@config/".Length).Replace('/', Path.DirectorySeparatorChar));
            if (value.StartsWith("@app/", StringComparison.OrdinalIgnoreCase))
                return Path.Combine(published, AppDirectoryName,
                    value.Substring("@app/".Length).Replace('/', Path.DirectorySeparatorChar));

            return value;
        }

        if (published is not null && !Path.IsPathRooted(value))
            return Path.GetFullPath(Path.Combine(published, value.Replace('/', Path.DirectorySeparatorChar)));

        return value;
    }

    public static string PluginDir(string pluginId)
        => Path.Combine(PluginsDir, pluginId);

    public static string PluginGenerationsDir(string pluginId)
        => Path.Combine(PluginDir(pluginId), "Generations");

    /// <summary>插件当前生效代目录：读 <c>Plugins/&lt;id&gt;/current</c>；缺指针回落最高 <c>gen-n</c>。</summary>
    public static string? PluginCurrentDir(string pluginId)
    {
        var pluginRoot = PluginDir(pluginId);
        var pointer = Path.Combine(pluginRoot, CurrentPointerFileName);
        if (File.Exists(pointer))
        {
            try
            {
                var name = File.ReadAllText(pointer).Trim();
                var candidate = Path.Combine(pluginRoot, "Generations", name);
                if (Directory.Exists(candidate))
                    return candidate;
            }
            catch
            {
                // 指针不可读 ⇒ 回落最高代
            }
        }

        var generations = PluginGenerationsDir(pluginId);
        if (!Directory.Exists(generations))
            return null;

        var best = 0;
        foreach (var dir in Directory.GetDirectories(generations))
        {
            var name = Path.GetFileName(dir);
            if (name.StartsWith("gen-", StringComparison.OrdinalIgnoreCase)
                && int.TryParse(name.Substring(4), out var n)
                && n > best)
            {
                best = n;
            }
        }

        return best > 0 ? Path.Combine(generations, "gen-" + best) : null;
    }

    /// <summary>重置探测缓存（测试/运维切换 Runtime 根后调用）。</summary>
    public static void Reset()
    {
        lock (SyncRoot)
        {
            _resolved = false;
            _publishedRoot = null;
            _dataRoot = null;
            _logsDir = null;
        }
    }

    private static System.Collections.Generic.IEnumerable<string> EnumerateStarts()
    {
        yield return AppContext.BaseDirectory;
        yield return Directory.GetCurrentDirectory();

        string? assemblyDir = null;
        try
        {
            var location = Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(location))
                assemblyDir = Path.GetDirectoryName(location);
        }
        catch
        {
            // 单文件/裁剪场景可能无 Location；忽略
        }

        if (!string.IsNullOrEmpty(assemblyDir))
            yield return assemblyDir!;
    }

    private static string? FindUpward(string? start)
    {
        if (string.IsNullOrWhiteSpace(start))
            return null;

        var current = start;
        while (!string.IsNullOrEmpty(current))
        {
            if (IsRuntimeRoot(current))
                return Path.GetFullPath(current!);

            var parent = Path.GetDirectoryName(current);
            if (string.IsNullOrEmpty(parent) || string.Equals(parent, current, StringComparison.OrdinalIgnoreCase))
                break;
            current = parent;
        }

        return null;
    }

    private static string? NormalizeRuntimeRoot(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate))
            return null;
        return IsRuntimeRoot(candidate) ? Path.GetFullPath(candidate) : null;
    }

    private static string ResolveDataRoot(string runtimeRoot)
    {
        foreach (var name in new[] { DataRootEnvironmentVariable, LegacyDataRootEnvironmentVariable })
        {
            var value = Environment.GetEnvironmentVariable(name);
            if (!string.IsNullOrWhiteSpace(value))
                return ResolveAgainst(runtimeRoot, value!);
        }

        var descriptorData = ReadDescriptorValue(runtimeRoot, "data");
        if (!string.IsNullOrWhiteSpace(descriptorData))
            return ResolveAgainst(runtimeRoot, descriptorData!);

        return Path.Combine(runtimeRoot, DataDirectoryName);
    }

    private static string ResolveLogsDir(string runtimeRoot)
    {
        var descriptorLogs = ReadDescriptorValue(runtimeRoot, "logs");
        if (!string.IsNullOrWhiteSpace(descriptorLogs))
            return ResolveAgainst(runtimeRoot, descriptorLogs!);

        return Path.Combine(DataRoot, "Logs");
    }

    private static string ResolveAgainst(string runtimeRoot, string candidate)
    {
        var value = candidate.Trim();
        return Path.IsPathRooted(value)
            ? Path.TrimEndingDirectorySeparator(Path.GetFullPath(value))
            : Path.TrimEndingDirectorySeparator(
                Path.GetFullPath(Path.Combine(runtimeRoot, value.Replace('/', Path.DirectorySeparatorChar))));
    }

    private static string? ReadDescriptorValue(string runtimeRoot, string name)
    {
        var descriptorPath = Path.Combine(runtimeRoot, RuntimeMarkerFileName);
        if (!File.Exists(descriptorPath))
            return null;

        try
        {
            using var document = JsonDocument.Parse(File.ReadAllText(descriptorPath));
            if (document.RootElement.ValueKind != JsonValueKind.Object)
                return null;
            foreach (var property in document.RootElement.EnumerateObject())
            {
                if (string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase)
                    && property.Value.ValueKind == JsonValueKind.String)
                {
                    return property.Value.GetString();
                }
            }
        }
        catch
        {
            // 描述符损坏 ⇒ 由后续默认值兜底（不伪造数据根）
        }
        return null;
    }

    /// <summary>双标记判据：避免 Runtime 位于源码仓内时把 Main 误判为发布根。</summary>
    private static bool IsRuntimeRoot(string dir)
        => File.Exists(Path.Combine(dir, RuntimeMarkerFileName))
           && File.Exists(Path.Combine(dir, EntryRelativePath.Replace('/', Path.DirectorySeparatorChar)));
}
