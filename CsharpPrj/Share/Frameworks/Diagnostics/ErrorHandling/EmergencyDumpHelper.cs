
using Common.Diagnostics;

namespace Common.Diagnostics
{
    public static class EmergencyDumpHelper
    {
        private static List<LogEntry>? TryGetLogs()
        {
            try
            {
                var logs = LogRepository.GetAll().ToList();
                if (logs.Count == 0)
                {
                    Console.WriteLine("[EmergencyDump] 内存中无日志，无需转储");
                    return null;
                }
                return logs;
            }
            catch
            {
                return null;
            }
        }

        public static void EmergencyDump(string? batchId = null, string? outputDir = null)
        {
            try
            {
                var logs = TryGetLogs();
                if (logs == null) return;

                var timestamp = DateTime.Now.ToString("yyyy-MM-dd_HH-mm-ss");
                var logsDir = outputDir ?? Path.Combine(Environment.CurrentDirectory, "logs");
                Directory.CreateDirectory(logsDir);

                var emergencyFile = Path.Combine(logsDir, $"emergency_fallback_{batchId ?? "unknown"}_{timestamp}.json");

                var exportData = new
                {
                    batchId = batchId ?? "unknown",
                    isEmergency = true,
                    isFallback = true,
                    exportedAt = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                    totalCount = logs.Count,
                    entries = logs.Select(e => new
                    {
                        timestamp = e.Timestamp.ToString("yyyy-MM-dd HH:mm:ss.fff"),
                        level = e.Level,
                        category = e.Category.ToString(),
                        module = e.Module,
                        message = e.Message
                    })
                };

                var json = System.Text.Json.JsonSerializer.Serialize(exportData, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(emergencyFile, json);
                Console.WriteLine($"[EmergencyDump] 已紧急转存 {logs.Count} 条日志到: {emergencyFile}");
            }
            catch (Exception dumpEx)
            {
                Console.WriteLine($"[EmergencyDump] 紧急转存失败: {dumpEx.Message}");
            }
        }

        public static void EmergencyDumpToSqlite(string? batchId)
        {
            try
            {
                var logs = TryGetLogs();
                if (logs == null) return;

                var filteredLogs = string.IsNullOrEmpty(batchId)
                    ? logs
                    : logs.Where(e => e.Extensions.TryGetValue("BatchId", out var val)
                        && val?.ToString() == batchId).ToList();

                if (filteredLogs.Count == 0)
                {
                    Console.WriteLine($"[EmergencyDump] 批次 '{batchId}' 在内存中无日志，无需转储");
                    return;
                }

                var storage = BatchRouter.Instance.GetIsolatedStorage();
                if (storage != null)
                {
                    storage.WriteBatch(filteredLogs);
                    Console.WriteLine($"[EmergencyDump] 已紧急转存 {filteredLogs.Count} 条日志到 SQLite（批次: {batchId ?? "all"}）");
                }
                else
                {
                    EmergencyDump(batchId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"[EmergencyDump] 紧急转存到 SQLite 失败: {ex.Message}");
                EmergencyDump(batchId);
            }
        }
    }
}
