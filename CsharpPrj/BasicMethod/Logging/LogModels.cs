
using System.Text.Json.Serialization;

namespace Common.Diagnostics
{

    [Flags]
    public enum LogCategory : ulong
    {
        None = 0,


        System = 1 << 0,

        Configuration = 1 << 1,

        Network = 1 << 2,

        Database = 1 << 3,

        Security = 1 << 4,

        Performance = 1 << 5,

        Memory = 1 << 6,

        Threading = 1 << 7,

        All = ulong.MaxValue
    }


    public class LogEntry
    {
        public DateTime Timestamp { get; set; } = DateTime.Now;

        public string Level { get; set; } = "INFO";

        public LogCategory Category { get; set; } = LogCategory.None;

        public string Module { get; set; } = string.Empty;

        public string Message { get; set; } = string.Empty;

        public string? SourceFile { get; set; }

        public int? SourceLine { get; set; }

        public string? SourceMember { get; set; }

        public Exception? Exception { get; set; }

        public Dictionary<string, object?> Extensions { get; set; } = new();

        public string? CategoryString { get; set; }

        [JsonIgnore]
        public long SequenceNumber { get; set; }

        public override string ToString()
        {
            var timestamp = Timestamp.ToString("HH:mm:ss");
            var source = SourceFile != null ? $" [{SourceFile}:{SourceLine}]" : "";
            return $"[{timestamp}] [{Level}] [{Module}] {Message}{source}";
        }

        public string ToFullString()
        {
            var timestamp = Timestamp.ToString("HH:mm:ss.fff");
            var source = SourceFile != null ? $" ({SourceFile}:{SourceLine} {SourceMember})" : "";
            var categoryName = GetCategoryDisplayName(Category);
            return $"[{timestamp}] [{Level}] [{Module}] [Category:{categoryName}] {Message}{source}";
        }

        private static string GetCategoryDisplayName(LogCategory category)
        {
            if (Enum.IsDefined(typeof(LogCategory), category))
                return category.ToString();

            if (LoggingSetup.ResolveCategoryDisplayName != null)
            {
                var resolved = LoggingSetup.ResolveCategoryDisplayName(category);
                if (resolved != null)
                    return resolved;
            }

            return $"0x{(ulong)(object)category:X}";
        }
    }


    public class LogQueryFilter
    {
        public string? Path { get; set; }

        public string? Level { get; set; }

        public string? Since { get; set; }

        public DateTime? FromTime { get; set; }

        public DateTime? ToTime { get; set; }

        public int Limit { get; set; } = 100;

        public string? Keyword { get; set; }

        public bool Recursive { get; set; } = true;

        public string? BatchId { get; set; }

        public DateTime? GetFromTime()
        {
            if (FromTime.HasValue)
                return FromTime;

            if (string.IsNullOrWhiteSpace(Since))
                return null;

            return ParseRelativeTime(Since);
        }

        private static DateTime? ParseRelativeTime(string since)
        {
            since = since.Trim().ToLowerInvariant();
            var now = DateTime.UtcNow;

            if (since.EndsWith("m") && int.TryParse(since.TrimEnd('m'), out var minutes))
                return now.AddMinutes(-minutes);
            if (since.EndsWith("h") && int.TryParse(since.TrimEnd('h'), out var hours))
                return now.AddHours(-hours);
            if (since.EndsWith("s") && int.TryParse(since.TrimEnd('s'), out var seconds))
                return now.AddSeconds(-seconds);
            if (since.EndsWith("d") && int.TryParse(since.TrimEnd('d'), out var days))
                return now.AddDays(-days);

            return null;
        }
    }


    public class LogExportResult
    {
        public List<string> ExportedFiles { get; set; } = new();

        public int TotalCount { get; set; }

        public bool Success { get; set; }
    }
}
