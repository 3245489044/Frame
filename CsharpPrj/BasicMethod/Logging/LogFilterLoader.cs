
using System.Text.Json;

namespace Common.Diagnostics
{
    public class LogFilterConfig
    {
        public string Version { get; set; } = "1.0";

        public string Description { get; set; } = "";

        public string Mode { get; set; } = "whitelist";

        public CategoryFilter Categories { get; set; } = new();

        public LevelFilter Levels { get; set; } = new();
    }

    public class CategoryFilter
    {
        public List<string> Enabled { get; set; } = new();

        public List<string> Disabled { get; set; } = new();
    }

    public class LevelFilter
    {
        /// <summary>控制台打印的最小级别（TRACE &lt; DEBUG &lt; INFO &lt; WARN &lt; ERROR &lt; FATAL）。
        /// 低于该级别的日志默认不打印，仅在排查时显式降级。</summary>
        public string Minimum { get; set; } = "INFO";

        public List<string> AlwaysPrint { get; set; } = new() { "ERROR", "FATAL" };
    }

    public static class LogFilterLoader
    {
        private static LogFilterConfig? _config;
        private static readonly object _lock = new();
        private static HashSet<string>? _enabledCategoryStrings;
        private static HashSet<string>? _disabledCategoryStrings;
        private static HashSet<string>? _alwaysPrintLevels;
        private static string _minimumLevel = "INFO";
        private static bool _loaded;
        private static string? _configPath;

        public static bool IsEnabled => _loaded && _config != null;

        public static string CurrentMode => _config?.Mode ?? "none";

        public static string? ConfigPath => _configPath;

        public static string MinimumLevel => _minimumLevel;

        public static bool Load(string jsonFilePath)
        {
            if (string.IsNullOrEmpty(jsonFilePath) || !File.Exists(jsonFilePath))
            {
                _loaded = false;
                _config = null;
                _configPath = null;
                System.Diagnostics.Debug.WriteLine($"[LogFilterLoader] 配置文件不存在: {jsonFilePath}，不过滤");
                return false;
            }

            lock (_lock)
            {
                try
                {
                    _configPath = jsonFilePath;
                    var json = File.ReadAllText(jsonFilePath);
                    _config = JsonSerializer.Deserialize<LogFilterConfig>(json, new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });

                    if (_config == null)
                    {
                        _loaded = false;
                        System.Diagnostics.Debug.WriteLine($"[LogFilterLoader] 配置文件解析失败（返回 null）: {jsonFilePath}");
                        return false;
                    }

                    _enabledCategoryStrings = _config.Categories?.Enabled
                        ?.Select(c => c.Trim())
                        .Where(c => !string.IsNullOrEmpty(c))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase)
                        ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    _disabledCategoryStrings = _config.Categories?.Disabled
                        ?.Select(c => c.Trim())
                        .Where(c => !string.IsNullOrEmpty(c))
                        .ToHashSet(StringComparer.OrdinalIgnoreCase)
                        ?? new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                    _alwaysPrintLevels = _config.Levels?.AlwaysPrint
                        ?.Select(l => l.ToUpperInvariant())
                        .ToHashSet() ?? new HashSet<string> { "ERROR", "FATAL" };

                    _minimumLevel = string.IsNullOrWhiteSpace(_config.Levels?.Minimum)
                        ? "INFO"
                        : _config.Levels.Minimum.ToUpperInvariant();

                    _loaded = true;

                    var disabledCount = _disabledCategoryStrings?.Count ?? 0;
                    var enabledCount = _enabledCategoryStrings?.Count ?? 0;
                    var alwaysPrintCount = _alwaysPrintLevels?.Count ?? 0;
                    System.Diagnostics.Debug.WriteLine(
                        $"[LogFilterLoader] 加载成功: mode={_config.Mode}, " +
                        $"minimum={_minimumLevel}, enabled={enabledCount}, disabled={disabledCount}, " +
                        $"alwaysPrint={alwaysPrintCount}, path={jsonFilePath}");

                    return true;
                }
                catch (Exception ex)
                {
                    System.Diagnostics.Debug.WriteLine($"[LogFilterLoader] 加载失败: {ex.Message}, path={jsonFilePath}");
                    _loaded = false;
                    _config = null;
                    return false;
                }
            }
        }

        public static bool ShouldPrint(LogEntry entry)
        {
            if (!_loaded || _config == null)
            {
                return true;
            }

            if (_alwaysPrintLevels?.Contains(entry.Level) == true)
            {
                return true;
            }

            // ★ 级别阈值：低于 Minimum 的日志（DEBUG 等）默认不打印，仅在排查时显式降级。
            //   先于类别过滤短路，避免对已过滤级别再做类别判断。
            if (LevelRank(entry.Level) < LevelRank(_minimumLevel))
            {
                return false;
            }

            var categoryStr = entry.CategoryString;
            if (string.IsNullOrEmpty(categoryStr))
            {
                categoryStr = entry.Category.ToString();
                System.Diagnostics.Debug.WriteLine(
                    $"[LogFilterLoader] Warning: CategoryString is empty, falling back to '{categoryStr}'. " +
                    $"Module={entry.Module}, Level={entry.Level}");
            }

            return _config.Mode?.ToLowerInvariant() switch
            {
                "whitelist" => _enabledCategoryStrings?.Contains(categoryStr) == true,
                "blacklist" => _disabledCategoryStrings?.Contains(categoryStr) != true,
                _ => true
            };
        }

        /// <summary>级别序（用于 Minimum 阈值比较）；未知级别按 INFO 处理，避免误过滤。</summary>
        private static int LevelRank(string level)
        {
            return (level?.ToUpperInvariant()) switch
            {
                "TRACE" => 0,
                "DEBUG" => 1,
                "INFO" => 2,
                "WARN" or "WARNING" => 3,
                "ERROR" => 4,
                "FATAL" => 5,
                _ => 2
            };
        }


        public static bool Reload(string jsonFilePath) => Load(jsonFilePath);

        public static void Reset()
        {
            lock (_lock)
            {
                _loaded = false;
                _config = null;
                _enabledCategoryStrings = null;
                _disabledCategoryStrings = null;
                _alwaysPrintLevels = null;
                _minimumLevel = "INFO";
            }
        }
    }
}
