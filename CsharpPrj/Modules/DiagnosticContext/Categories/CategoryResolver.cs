
using System.Text.Json;
using Common.Diagnostics;

namespace Modules.DiagnosticContext
{
    public class CategoryResolver
    {
        public static readonly CategoryResolver Instance = new();

        private Dictionary<string, string>? _aliasCache;
        private readonly object _cacheLock = new();

        public const string ConfigFileName = "config/category-aliases.json";

        private static string GetConfigPath()
        {
            var settingsPath = DiagnosticsConfigReader.GetSettingsPath(typeof(CategoryResolver));
            if (settingsPath != null)
            {
                var aliasesPath = DiagnosticsConfigReader.GetString("category_aliases/path", typeof(CategoryResolver));
                if (!string.IsNullOrEmpty(aliasesPath) && File.Exists(aliasesPath))
                    return aliasesPath;
            }

            var assemblyPath = typeof(CategoryResolver).Assembly.Location;
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

            return Path.Combine(Directory.GetCurrentDirectory(), ConfigFileName);
        }

        public LogCategory Resolve(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
                return LogCategory.None;

            var result = LogCategoryRegistry.ResolveEnumName(name);
            if (result != LogCategory.None)
                return result;

            var lowerName = name.ToLowerInvariant();
            var aliases = LoadAliases();
            if (aliases.TryGetValue(lowerName, out var enumName))
            {
                result = LogCategoryRegistry.ResolveEnumName(enumName);
                if (result != LogCategory.None)
                    return result;
            }

            return LogCategory.None;
        }

        public string GetModuleName(LogCategory category)
        {
            var path = LogCategoryRegistry.GetPath((ulong)category);
            if (path != null)
            {
                var segments = path.Split('/');
                if (segments.Length > 0)
                    return segments[0];
            }
            return "General";
        }

        private Dictionary<string, string> LoadAliases()
        {
            if (_aliasCache != null)
                return _aliasCache;

            lock (_cacheLock)
            {
                if (_aliasCache != null)
                    return _aliasCache;

                var path = GetConfigPath();
                if (!File.Exists(path))
                {
                    _aliasCache = new Dictionary<string, string>();
                    return _aliasCache;
                }

                try
                {
                    var json = File.ReadAllText(path);
                    var config = JsonSerializer.Deserialize<AliasConfig>(json);
                    if (config?.Aliases != null)
                    {
                        _aliasCache = new Dictionary<string, string>(config.Aliases, StringComparer.OrdinalIgnoreCase);
                    }
                    else
                    {
                        _aliasCache = new Dictionary<string, string>();
                    }
                }
                catch
                {
                    _aliasCache = new Dictionary<string, string>();
                }

                return _aliasCache;
            }
        }

        private class AliasConfig
        {
            public string? Version { get; set; }
            public string? Description { get; set; }
            public Dictionary<string, string>? Aliases { get; set; }
        }
    }
}
