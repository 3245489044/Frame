
using System.Text.Json;
using Common.Diagnostics;
using Modules.DiagnosticContext.Formatting;

namespace Modules.DiagnosticContext
{
    public static class LogJsonFormatter
    {
        public static string FormatCategoriesJson()
        {
            var categories = LogCategoryRegistry.GetLeafNodes()
                .OrderBy(n => n.FullPath)
                .Select(n => new
                {
                    path = n.FullPath,
                    description = n.Description,
                    category = n.Category.ToString()
                })
                .ToList();

            return JsonSerializer.Serialize(new
            {
                count = categories.Count,
                categories
            }, new JsonSerializerOptions
            {
                WriteIndented = true,
                Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping
            });
        }

        public static string QueryToJson(
            string? path,
            string? level,
            string? since,
            int limit,
            string? keyword,
            bool compact)
        {
            var filter = new LogQueryFilter
            {
                Path = path ?? string.Empty,
                Level = level,
                Since = since,
                Limit = limit,
                Keyword = keyword,
                Recursive = true
            };

            var result = LogQueryFS.Query(filter);

            var options = compact ? JsonOptions.CompactRelaxed : JsonOptions.UnsafeRelaxed;

            return JsonSerializer.Serialize(new
            {
                queryPath = result.QueryPath,
                totalCount = result.TotalCount,
                returnedCount = result.Entries.Count,
                hasMore = result.HasMore,
                elapsedMs = result.Elapsed.TotalMilliseconds,
                entries = result.Entries.Select(e => new
                {
                    timestamp = e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    level = e.Level,
                    category = e.Category.ToString(),
                    module = e.Module,
                    message = e.Message,
                    exception = e.Exception?.Message
                })
            }, options);
        }
    }
}
