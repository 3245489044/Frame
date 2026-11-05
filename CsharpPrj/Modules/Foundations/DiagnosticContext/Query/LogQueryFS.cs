
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Common.Diagnostics;
using Modules.DiagnosticContext.Formatting;

namespace Modules.DiagnosticContext
{

    public class LogQueryResult
    {
        public string QueryPath { get; set; } = string.Empty;

        public List<string> MatchedCategories { get; set; } = new();

        public List<LogEntry> Entries { get; set; } = new();

        public int TotalCount { get; set; }

        public TimeSpan Elapsed { get; set; }

        public bool HasMore { get; set; }

        public string ToFormattedText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Query: {QueryPath}");
            sb.AppendLine($"Matched: {string.Join(", ", MatchedCategories)}");
            sb.AppendLine($"Total: {TotalCount}, Returned: {Entries.Count}, Elapsed: {Elapsed.TotalMilliseconds:F1}ms");
            sb.AppendLine();

            foreach (var entry in Entries)
            {
                var level = entry.Level.ToString().PadRight(5);
                var time = entry.Timestamp.ToString("HH:mm:ss.fff");
                var category = LogCategoryHelper.ToDisplayName(entry.Category);
                sb.AppendLine($"[{level}] {time} [{category}] {entry.Message}");
                if (entry.Exception != null)
                {
                    sb.AppendLine($"       Exception: {entry.Exception.Message}");
                }
            }

            return sb.ToString();
        }

        public string ToJson()
        {
            var options = JsonOptions.UnsafeRelaxedWithConverter(new LogEntryJsonConverter());
            return System.Text.Json.JsonSerializer.Serialize(Entries, options);
        }
    }


    public class LogStats
    {
        public string Path { get; set; } = string.Empty;

        public int TotalCount { get; set; }

        public Dictionary<string, int> ByLevel { get; set; } = new();

        public DateTime? LastLogTime { get; set; }

        public DateTime? FirstLogTime { get; set; }

        public Dictionary<string, LogStats> Children { get; set; } = new();

        public string ToFormattedText()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"Path: {Path}");
            sb.AppendLine($"Total: {TotalCount}");
            sb.AppendLine($"By Level: {string.Join(", ", ByLevel.Select(kv => $"{kv.Key}={kv.Value}"))}");
            if (FirstLogTime.HasValue)
                sb.AppendLine($"From: {FirstLogTime.Value:HH:mm:ss.fff}");
            if (LastLogTime.HasValue)
                sb.AppendLine($"To: {LastLogTime.Value:HH:mm:ss.fff}");
            return sb.ToString();
        }
    }


    public class LogQueryFS
    {
        public static Func<Func<LogEntry, bool>, IEnumerable<LogEntry>>? QueryExecutor { get; set; }

        public static IEnumerable<string> ListDirectories(string path)
        {
            return LogCategoryRegistry.ListDirectories(path);
        }

        public static LogQueryResult Query(LogQueryFilter filter)
        {
            if (filter == null)
                throw new ArgumentNullException(nameof(filter));

            var sw = System.Diagnostics.Stopwatch.StartNew();
            var result = new LogQueryResult
            {
                QueryPath = filter.Path ?? "/"
            };

            var path = filter.Path ?? string.Empty;
            var categories = ResolvePathToCategories(path, filter.Recursive);

            if (!string.IsNullOrWhiteSpace(path))
            {
                var node = LogCategoryRegistry.GetNode(path);
                if (node != null)
                {
                    if (node.IsLeaf)
                    {
                        result.MatchedCategories.Add(node.FullPath);
                    }
                    else if (filter.Recursive)
                    {
                        CollectMatchedPaths(node, result.MatchedCategories);
                    }
                    else
                    {
                        result.MatchedCategories.Add(node.FullPath);
                    }
                }
                else
                {
                    result.MatchedCategories.Add(path);
                }
            }

            Func<LogEntry, bool> predicate = entry =>
            {
                if (categories.Count > 0)
                {
                    if (!categories.Any(cat => (entry.Category & cat) != 0))
                        return false;
                }

                if (!string.IsNullOrWhiteSpace(filter.Level))
                {
                    var levels = filter.Level.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
                    if (!levels.Contains(entry.Level, StringComparer.OrdinalIgnoreCase))
                        return false;
                }

                var fromTime = filter.GetFromTime();
                if (fromTime.HasValue && entry.Timestamp < fromTime.Value)
                    return false;
                if (filter.ToTime.HasValue && entry.Timestamp > filter.ToTime.Value)
                    return false;

                if (!string.IsNullOrWhiteSpace(filter.Keyword))
                {
                    if (!entry.Message.Contains(filter.Keyword, StringComparison.OrdinalIgnoreCase))
                        return false;
                }

                if (!string.IsNullOrWhiteSpace(filter.BatchId))
                {
                    if (!entry.Extensions.TryGetValue("BatchId", out var batchId) ||
                        batchId?.ToString() != filter.BatchId)
                        return false;
                }

                return true;
            };

            var entries = ExecuteQuery(predicate);

            var allEntries = entries.ToList();
            result.TotalCount = allEntries.Count;

            if (filter.Limit > 0)
            {
                result.Entries = allEntries.Take(filter.Limit).ToList();
                result.HasMore = allEntries.Count > filter.Limit;
            }
            else
            {
                result.Entries = allEntries;
                result.HasMore = false;
            }

            sw.Stop();
            result.Elapsed = sw.Elapsed;

            return result;
        }

        public static LogStats GetStats(string path)
        {
            var stats = new LogStats { Path = path };
            var entries = Query(new LogQueryFilter { Path = path, Limit = 0 });

            stats.TotalCount = entries.TotalCount;

            foreach (var entry in entries.Entries)
            {
                if (!stats.ByLevel.ContainsKey(entry.Level))
                    stats.ByLevel[entry.Level] = 0;
                stats.ByLevel[entry.Level]++;

                if (!stats.FirstLogTime.HasValue || entry.Timestamp < stats.FirstLogTime.Value)
                    stats.FirstLogTime = entry.Timestamp;
                if (!stats.LastLogTime.HasValue || entry.Timestamp > stats.LastLogTime.Value)
                    stats.LastLogTime = entry.Timestamp;
            }

            return stats;
        }

        private static List<LogCategory> ResolvePathToCategories(string path, bool recursive)
        {
            var categories = new List<LogCategory>();

            if (string.IsNullOrWhiteSpace(path))
                return categories;

            var node = LogCategoryRegistry.GetNode(path);
            if (node != null)
            {
                if (node.IsLeaf)
                {
                    categories.Add(node.Category);
                }
                else if (recursive)
                {
                    CollectLeafCategories(node, categories);
                }
                else
                {
                    categories.Add(node.Category);
                }
            }

            return categories;
        }

        private static void CollectLeafData<T>(CategoryNode node, List<T> result, Func<CategoryNode, T> selector)
        {
            if (node.IsLeaf)
            {
                result.Add(selector(node));
                return;
            }

            foreach (var child in node.Children)
            {
                CollectLeafData(child, result, selector);
            }
        }

        private static void CollectLeafCategories(CategoryNode node, List<LogCategory> categories)
            => CollectLeafData(node, categories, n => n.Category);

        private static void CollectMatchedPaths(CategoryNode node, List<string> paths)
            => CollectLeafData(node, paths, n => n.FullPath);

        private static IEnumerable<LogEntry> ExecuteQuery(Func<LogEntry, bool> predicate)
        {
            if (QueryExecutor != null)
            {
                return QueryExecutor(predicate);
            }
            else
            {
                return LogRepository.Query(predicate);
            }
        }
    }


    public static class LogCategoryHelper
    {
        public static string ToDisplayName(LogCategory category)
        {
            return LogCategoryRegistry.GetDisplayName(category);
        }
    }


    public class LogEntryJsonConverter : JsonConverter<LogEntry>
    {
        public override LogEntry? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return JsonSerializer.Deserialize<LogEntry>(ref reader, options);
        }

        public override void Write(Utf8JsonWriter writer, LogEntry value, JsonSerializerOptions options)
        {
            writer.WriteStartObject();

            writer.WriteString("timestamp", value.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"));
            writer.WriteString("level", value.Level);
            writer.WriteString("category", LogCategoryHelper.ToDisplayName(value.Category));
            writer.WriteString("module", value.Module);
            writer.WriteString("message", value.Message);

            if (value.SourceFile != null)
                writer.WriteString("sourceFile", value.SourceFile);
            if (value.SourceLine.HasValue)
                writer.WriteNumber("sourceLine", value.SourceLine.Value);
            if (value.SourceMember != null)
                writer.WriteString("sourceMember", value.SourceMember);
            if (value.Exception != null)
                writer.WriteString("exception", value.Exception.Message);

            writer.WriteEndObject();
        }
    }
}
