
using System.Text.Json;
using Common.Diagnostics;

namespace Common.PlaceholderFramework.Core
{
    public static class LogCategoryResolver
    {
        private static Dictionary<string, LogCategory>? _aliasCache;
        private static readonly object _cacheLock = new();
        private static bool _cacheInitialized;

        private static void EnsureCacheInitialized()
        {
            if (_cacheInitialized) return;

            lock (_cacheLock)
            {
                if (_cacheInitialized) return;

                _aliasCache = new Dictionary<string, LogCategory>(StringComparer.OrdinalIgnoreCase);

                foreach (LogCategory value in Enum.GetValues<LogCategory>())
                {
                    if (value == LogCategory.None || value == LogCategory.All) continue;
                    var name = value.ToString().ToLowerInvariant();
                    _aliasCache[name] = value;
                    _aliasCache[$"system.{name}"] = value;
                }

                var enumNameMap = Modules.DiagnosticContext.LogCategoryRegistry.GetEnumNameMap();
                foreach (var kvp in enumNameMap)
                {
                    var name = kvp.Key.ToLowerInvariant();
                    _aliasCache[name] = kvp.Value;
                }

                var configPath = GetAliasesConfigPath();
                if (configPath != null && File.Exists(configPath))
                {
                    try
                    {
                        var json = File.ReadAllText(configPath);
                        var config = JsonSerializer.Deserialize<AliasesConfig>(json);
                        if (config?.Aliases != null)
                        {
                            foreach (var (alias, enumName) in config.Aliases)
                            {
                                var lowerAlias = alias.ToLowerInvariant();

                                var resolved = Modules.DiagnosticContext.LogCategoryRegistry.ResolveEnumName(enumName);
                                if (resolved != LogCategory.None)
                                {
                                    _aliasCache[lowerAlias] = resolved;
                                }
                                else if (Enum.TryParse<LogCategory>(enumName, ignoreCase: true, out var catVal))
                                {
                                    _aliasCache[lowerAlias] = catVal;
                                }
                            }
                        }
                    }
                    catch (JsonException)
                    {
                    }
                }

                _cacheInitialized = true;
            }
        }

        private static string? GetAliasesConfigPath()
        {
            var probeDir = Path.GetDirectoryName(typeof(LogCategoryResolver).Assembly.Location);
            while (probeDir != null)
            {
                var candidate = Path.Combine(probeDir, "FileDriven", "DiagnosticsConfig", "category-aliases.json");
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);

                var parent = Directory.GetParent(probeDir);
                if (parent == null || parent.FullName == probeDir) break;
                probeDir = parent.FullName;
            }

            var cwdPath = Path.Combine(Directory.GetCurrentDirectory(), "FileDriven", "DiagnosticsConfig", "category-aliases.json");
            if (File.Exists(cwdPath))
                return Path.GetFullPath(cwdPath);

            return null;
        }



        private class AliasesConfig
        {
            public string? Version { get; set; }
            public string? Description { get; set; }
            public Dictionary<string, string>? Aliases { get; set; }
        }

        public static LogCategory Resolve(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
                return LogCategory.System;

            if (Enum.TryParse<LogCategory>(category, ignoreCase: true, out var parsedCategory))
            {
                if (Enum.IsDefined(typeof(LogCategory), parsedCategory))
                    return parsedCategory;

                if (parsedCategory != LogCategory.None)
                {
                    var definedValues = Enum.GetValues<LogCategory>()
                        .Where(v => v != LogCategory.None && v != LogCategory.All)
                        .ToList();

                    ulong combinedValue = (ulong)(object)parsedCategory;
                    ulong definedBits = 0;
                    foreach (var dv in definedValues)
                    {
                        definedBits |= (ulong)(object)dv;
                    }

                    if ((combinedValue & ~definedBits) == 0)
                        return parsedCategory;
                }
            }

            EnsureCacheInitialized();
            var lowerName = category.ToLowerInvariant();

            if (_aliasCache!.TryGetValue(lowerName, out var resolved))
                return resolved;

            try
            {
                var registryResolved = Modules.DiagnosticContext.LogCategoryRegistry.Resolve(lowerName);
                if (registryResolved != LogCategory.None)
                    return registryResolved;
            }
            catch
            {
            }

            return LogCategory.System;
        }

        public static string GetModuleName(LogCategory category)
        {
            try
            {
                var path = Modules.DiagnosticContext.LogCategoryRegistry.GetPath((ulong)category);
                if (path != null)
                {
                    var segments = path.Split('/');
                    if (segments.Length > 0)
                        return segments[0];
                }
            }
            catch
            {
            }

            if (IsModuleCategory(category, "FileDriven")) return "FileDriven";
            if (IsModuleCategory(category, "WdUI")) return "WdUI";
            if (IsModuleCategory(category, "Launcher")) return "Launcher";
            if (IsModuleCategory(category, "FunctionStorage")) return "FunctionStorage";
            if (IsModuleCategory(category, "Batch")) return "Batch";
            if (IsSystemCategory(category)) return "System";
            return "General";
        }

        private static bool IsModuleCategory(LogCategory category, string moduleName)
        {
            var moduleMask = Modules.DiagnosticContext.LogCategoryRegistry.GetModuleMask(moduleName);
            if ((ulong)(object)moduleMask == 0)
                return false;

            var val = (ulong)(object)category;
            return (val & (ulong)(object)moduleMask) != 0 && (val & ~(ulong)(object)moduleMask) == 0;
        }

        private static bool IsSystemCategory(LogCategory category)
        {
            var val = (ulong)(object)category;
            return (val & (ulong)(object)LogCategory.System) != 0;
        }
    }
}
