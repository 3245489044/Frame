
using System.Text.Json;

namespace Modules.DiagnosticContext
{
    public static class DiagnosticsConfigReader
    {
        public const string ConfigFileName = "config/diagnostics-settings.json";

        private static JsonDocument? _cachedDoc;
        private static string? _cachedSettingsPath;
        private static readonly object _cacheLock = new();

        public static string? GetSettingsPath(Type? callerType = null)
        {
            try
            {
                // L7 修复：FileDrivenPaths 下沉至 BasicMethod，消除对 Operation 层 FrontEndDriven 的反射依赖。
                var path = PathManager.FileDrivenPaths.GetDiagnosticsSettingsPath();
                if (!string.IsNullOrEmpty(path) && File.Exists(path))
                    return path;
            }
            catch
            {
            }

            var assemblyType = callerType ?? typeof(DiagnosticsConfigReader);
            var assemblyPath = assemblyType.Assembly.Location;
            if (!string.IsNullOrEmpty(assemblyPath))
            {
                var dir = Path.GetDirectoryName(assemblyPath);
                if (dir != null)
                {
                    var configPath = Path.Combine(dir, ConfigFileName);
                    if (File.Exists(configPath))
                        return configPath;
                }
            }

            var cwdPath = Path.Combine(Directory.GetCurrentDirectory(), ConfigFileName);
            if (File.Exists(cwdPath))
                return cwdPath;

            return null;
        }

        public static string? GetString(string propertyPath, Type? callerType = null)
        {
            var doc = GetJsonDocument(callerType);
            if (doc == null) return null;

            var segments = propertyPath.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
            var element = doc.RootElement;

            foreach (var segment in segments)
            {
                if (!element.TryGetProperty(segment, out element))
                    return null;
            }

            return element.GetString();
        }

        public static string? GetNestedString(Type? callerType, params string[] pathSegments)
        {
            var doc = GetJsonDocument(callerType);
            if (doc == null) return null;

            var element = doc.RootElement;
            foreach (var segment in pathSegments)
            {
                if (!element.TryGetProperty(segment, out element))
                    return null;
            }

            return element.GetString();
        }

        private static JsonDocument? GetJsonDocument(Type? callerType)
        {
            var settingsPath = GetSettingsPath(callerType);
            if (settingsPath == null || !File.Exists(settingsPath))
                return null;

            if (_cachedSettingsPath == settingsPath && _cachedDoc != null)
                return _cachedDoc;

            lock (_cacheLock)
            {
                if (_cachedSettingsPath == settingsPath && _cachedDoc != null)
                    return _cachedDoc;

                try
                {
                    var json = File.ReadAllText(settingsPath);
                    _cachedDoc = JsonDocument.Parse(json);
                    _cachedSettingsPath = settingsPath;
                    return _cachedDoc;
                }
                catch
                {
                    return null;
                }
            }
        }

        public static void ClearCache()
        {
            lock (_cacheLock)
            {
                _cachedDoc?.Dispose();
                _cachedDoc = null;
                _cachedSettingsPath = null;
            }
        }
    }
}
