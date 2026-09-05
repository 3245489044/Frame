
using System.Text.Json;

namespace PathManager;

/// <summary>
/// FileDriven 路径基础设施（L7 下沉）：消除 Modules/BasicMethod 对 Operation 层
/// FrontEndDriven.Shared.Utils.PathManager 的反射依赖（[CR: axioms:AX-CORE.AxiomHierarchyStructure@align] 单向依赖）。
/// 核心逻辑从 FrontEndDriven.PathManager 提取：ProjectRoot 定位 + framework.config.json paths 覆盖 + 缓存。
/// </summary>
public static class FileDrivenPaths
{
    private static string? _cachedProjectRoot;
    private static PathOverrides? _cachedOverrides;
    private static readonly object _lock = new();

    private sealed class PathOverrides
    {
        public string? ProjectRoot;
        public string? JsonConfig;
        public string? LogMessages;
        public string? LogFilterConfig;
        public string? DiagnosticsConfig;
    }

    public static string GetProjectRoot()
    {
        if (_cachedProjectRoot != null) return _cachedProjectRoot;

        lock (_lock)
        {
            if (_cachedProjectRoot != null) return _cachedProjectRoot;

            var overrides = LoadOverrides();
            if (!string.IsNullOrEmpty(overrides?.ProjectRoot) && Directory.Exists(overrides.ProjectRoot))
            {
                _cachedProjectRoot = overrides.ProjectRoot;
                return _cachedProjectRoot;
            }

            _cachedProjectRoot = ProjectPathManager.FindProjectRoot() ?? Directory.GetCurrentDirectory();
            return _cachedProjectRoot;
        }
    }

    public static string GetJsonConfigDir()
        => ResolveAbsolute(LoadOverrides()?.JsonConfig ?? "FileDriven/DrivenConfig/JsonConfig");

    public static string GetLogMessagesFilePath()
        => ResolveAbsolute(LoadOverrides()?.LogMessages ?? "FileDriven/filedriven_log_messages.json");

    public static string GetLogFilterConfigPath()
        => ResolveAbsolute(LoadOverrides()?.LogFilterConfig ?? "FileDriven/LogFilterConfig.json");

    public static string GetDiagnosticsConfigDir()
        => ResolveAbsolute(LoadOverrides()?.DiagnosticsConfig ?? "FileDriven/DiagnosticsConfig");

    public static string GetDiagnosticsSettingsPath()
        => Path.Combine(GetDiagnosticsConfigDir(), "diagnostics-settings.json");

    public static void ResetCache()
    {
        lock (_lock)
        {
            _cachedProjectRoot = null;
            _cachedOverrides = null;
        }
    }

    private static string ResolveAbsolute(string relativePath)
    {
        if (Path.IsPathRooted(relativePath))
            return relativePath;

        return Path.GetFullPath(Path.Combine(GetProjectRoot(), relativePath));
    }

    private static PathOverrides? LoadOverrides()
    {
        if (_cachedOverrides != null)
            return _cachedOverrides;

        lock (_lock)
        {
            if (_cachedOverrides != null)
                return _cachedOverrides;

            try
            {
                var configPath = FindFileUpward("framework.config.json");
                if (configPath == null || !File.Exists(configPath))
                    return null;

                var json = File.ReadAllText(configPath);
                using var doc = JsonDocument.Parse(json);
                var pathsElement = doc.RootElement.TryGetProperty("paths", out var p) ? p : default;
                if (pathsElement.ValueKind != JsonValueKind.Object)
                    return null;

                var overrides = new PathOverrides();
                if (pathsElement.TryGetProperty("projectRoot", out var pr) && pr.ValueKind == JsonValueKind.String)
                    overrides.ProjectRoot = pr.GetString();
                if (pathsElement.TryGetProperty("jsonConfig", out var jc) && jc.ValueKind == JsonValueKind.String)
                    overrides.JsonConfig = jc.GetString();
                if (pathsElement.TryGetProperty("logMessages", out var lm) && lm.ValueKind == JsonValueKind.String)
                    overrides.LogMessages = lm.GetString();
                if (pathsElement.TryGetProperty("logFilterConfig", out var lf) && lf.ValueKind == JsonValueKind.String)
                    overrides.LogFilterConfig = lf.GetString();
                if (pathsElement.TryGetProperty("diagnosticsConfig", out var dg) && dg.ValueKind == JsonValueKind.String)
                    overrides.DiagnosticsConfig = dg.GetString();

                _cachedOverrides = overrides;
                return overrides;
            }
            catch
            {
                return null;
            }
        }
    }

    private static string? FindFileUpward(string fileName)
    {
        foreach (var start in new[] { AppContext.BaseDirectory, Directory.GetCurrentDirectory() })
        {
            var current = start;
            while (!string.IsNullOrEmpty(current))
            {
                var candidate = Path.Combine(current, fileName);
                if (File.Exists(candidate))
                    return candidate;

                var parent = Path.GetDirectoryName(current);
                if (parent == current || parent == null) break;
                current = parent;
            }
        }

        return null;
    }
}
