using System.Text;
using System.Text.Json;
using CodePathExplorer.Models;

namespace CodePathExplorer.Export;

public class LogExporter
{
    public string ExportToLog(AnalysisResult result, string logOutputDir, string batchId)
    {
        Directory.CreateDirectory(logOutputDir);

        var entries = new List<LogEntry>();

        entries.Add(new LogEntry
        {
            Timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffK"),
            Level = "INFO",
            Category = "CodePathExplorer.Summary",
            Module = "CodePathExplorer",
            Message = $"分析完成: 扫描 {result.FileCount} 个文件，发现 {result.ClassCount} 个类，生成 {result.CallChains.Count} 条调用链，{result.FieldFlowSets.Count} 个字段数据流"
        });

        var chainGroups = result.CallChains.GroupBy(c => c.EntryMethod);
        foreach (var group in chainGroups)
        {
            entries.Add(new LogEntry
            {
                Timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffK"),
                Level = "INFO",
                Category = "CodePathExplorer.CallChain",
                Module = "CodePathExplorer",
                Message = $"[入口] {group.Key} ({group.Count()} 条路径)"
            });

            foreach (var chain in group)
            {
                entries.Add(new LogEntry
                {
                    Timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffK"),
                    Level = "INFO",
                    Category = "CodePathExplorer.CallChain",
                    Module = "CodePathExplorer",
                    Message = $"  Chain {chain.ChainId}: {chain.PathDescription}"
                });
            }
        }

        foreach (var fieldSet in result.FieldFlowSets)
        {
            string status = fieldSet.IsUnused ? " [冗余]" :
                            fieldSet.IsSingleMethodUse ? " [可内化]" : "";

            entries.Add(new LogEntry
            {
                Timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffK"),
                Level = fieldSet.IsUnused ? "WARN" : "INFO",
                Category = "CodePathExplorer.FieldFlow",
                Module = "CodePathExplorer",
                Message = $"字段 {fieldSet.ClassName}.{fieldSet.FieldName} ({fieldSet.FieldType}){status}"
            });

            foreach (var flow in fieldSet.Flows)
            {
                entries.Add(new LogEntry
                {
                    Timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffK"),
                    Level = "INFO",
                    Category = "CodePathExplorer.FieldFlow",
                    Module = "CodePathExplorer",
                    Message = $"  Flow {flow.FlowId}: {flow.FlowDescription}"
                });
            }
        }

        foreach (var suggestion in result.Suggestions)
        {
            var level = suggestion.Type switch
            {
                SuggestionType.RedundantField => "WARN",
                SuggestionType.RedundantMethod => "WARN",
                SuggestionType.InternalizableField => "INFO",
                SuggestionType.Simplifiable => "INFO",
                _ => "INFO"
            };

            entries.Add(new LogEntry
            {
                Timestamp = DateTime.Now.ToString("yyyy-MM-ddTHH:mm:ss.fffK"),
                Level = level,
                Category = "CodePathExplorer.Suggestion",
                Module = "CodePathExplorer",
                Message = $"[{suggestion.Type}] {suggestion.Description}"
            });
        }

        var logData = new LogData
        {
            BatchId = batchId,
            BatchName = $"代码路径分析 {DateTime.Now:yyyy/M/d HH:mm:ss}",
            BatchSource = "交互启动.bat",
            BatchDesc = "CodePathExplorer 代码路径分析",
            TotalCount = entries.Count,
            Summary = new LogSummary
            {
                ByLevel = entries.GroupBy(e => e.Level)
                    .ToDictionary(g => g.Key, g => g.Count()),
                ByModule = entries.GroupBy(e => e.Module)
                    .ToDictionary(g => g.Key, g => g.Count()),
                ByCategory = entries.GroupBy(e => e.Category)
                    .ToDictionary(g => g.Key, g => g.Count())
            },
            Entries = entries
        };

        var filePath = Path.Combine(logOutputDir, $"batch_{batchId}_latest.json");
        var jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase
        };
        var json = JsonSerializer.Serialize(logData, jsonOptions);
        File.WriteAllText(filePath, json, Encoding.UTF8);

        return filePath;
    }

    public static string GenerateBatchId()
    {
        return $"CodePathAnalysis_{DateTime.Now:yyyyMMdd_HHmmss}";
    }
}


public class LogData
{
    public string BatchId { get; set; } = "";
    public string BatchName { get; set; } = "";
    public string BatchSource { get; set; } = "";
    public string BatchDesc { get; set; } = "";
    public int TotalCount { get; set; }
    public LogSummary Summary { get; set; } = new();
    public List<LogEntry> Entries { get; set; } = new();
}

public class LogSummary
{
    public Dictionary<string, int> ByLevel { get; set; } = new();
    public Dictionary<string, int> ByModule { get; set; } = new();
    public Dictionary<string, int> ByCategory { get; set; } = new();
}

public class LogEntry
{
    public string Timestamp { get; set; } = "";
    public string Level { get; set; } = "";
    public string Category { get; set; } = "";
    public string Module { get; set; } = "";
    public string Message { get; set; } = "";
}
