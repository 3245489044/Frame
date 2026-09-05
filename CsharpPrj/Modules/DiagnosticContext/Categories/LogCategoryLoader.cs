
using System.Text.Json;
using Common.Diagnostics;

namespace Modules.DiagnosticContext
{
    public static class LogCategoryLoader
    {
        private static string? GetDefaultConfigPath()
        {

            var probeDir = Path.GetDirectoryName(typeof(LogCategoryLoader).Assembly.Location);
            while (probeDir != null)
            {
                var candidate = Path.Combine(probeDir, "FileDriven", "DiagnosticsConfig", "log-categories.json");
                if (File.Exists(candidate))
                    return Path.GetFullPath(candidate);

                var parent = Directory.GetParent(probeDir);
                if (parent == null || parent.FullName == probeDir) break;
                probeDir = parent.FullName;
            }

            var cwdPath = Path.Combine(Directory.GetCurrentDirectory(), "FileDriven", "DiagnosticsConfig", "log-categories.json");
            if (File.Exists(cwdPath))
                return Path.GetFullPath(cwdPath);

            return null;
        }



        private class LogCategoryConfig
        {
            public string? Version { get; set; }
            public string? Description { get; set; }
            public Dictionary<string, ModuleConfig>? Modules { get; set; }
        }

        private class ModuleConfig
        {
            public string? Description { get; set; }
            public Dictionary<string, CategoryConfig>? Categories { get; set; }
        }

        private class CategoryConfig
        {
            public string? Description { get; set; }
        }


        public static (string path, string enumName, string description)[] LoadDefinitions()
        {
            var configPath = GetDefaultConfigPath();
            if (configPath == null || !File.Exists(configPath))
                return Array.Empty<(string, string, string)>();

            try
            {
                var json = File.ReadAllText(configPath);
                var config = JsonSerializer.Deserialize<LogCategoryConfig>(json);
                if (config?.Modules == null || config.Modules.Count == 0)
                    return Array.Empty<(string, string, string)>();

                var definitions = new List<(string, string, string)>();
                foreach (var (moduleName, moduleConfig) in config.Modules)
                {
                    if (moduleConfig.Categories == null) continue;
                    foreach (var (catName, catConfig) in moduleConfig.Categories)
                    {
                        var path = $"{moduleName}/{catName}";
                        var enumName = $"{moduleName}_{catName}";
                        var description = catConfig.Description ?? string.Empty;
                        definitions.Add((path, enumName, description));
                    }
                }
                return definitions.ToArray();
            }
            catch (JsonException)
            {
                return Array.Empty<(string, string, string)>();
            }
        }
    }
}
