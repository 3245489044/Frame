using System;
using System.Collections.Generic;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.Json.Nodes;
using Share.TaskStatusPool.TaskPool;

namespace FrontEndDriven.Rendering;

/// <summary>
/// ur_tasks 任务池分页 → Markdown 渲染器：生成「标题 + 概览表(Id/TaskKey/Priority/MaxRetries/Status/时间戳) + 逐行 pretty JSON 代码块」的
/// 可读文档。纯函数，供 <c>Plate.MarkdownViewer</c> 控件宿主与测试复用；坏 JSON 不阻断整页。
/// 依赖 TaskStatusPool（TaskPoolRowView / TaskExecutionStatus），与 MarkdownViewer 纯 md 渲染解耦。</summary>
public static class TaskPoolMarkdownRenderer
{
    // pretty + 中文原样（UnsafeRelaxedJsonEscaping）：修复 ur_tasks PayloadJson 的 \uXXXX 转义可读性问题。
    private static readonly JsonSerializerOptions PrettyJson = new()
    {
        WriteIndented = true,
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
    };

    /// <summary>把一页任务池行渲染为 Markdown 字符串（标题 + 概览表 + 每行详情）。</summary>
    public static string RenderPage(IReadOnlyList<TaskPoolRowView> rows, int pageNumber, int total)
    {
        var sb = new StringBuilder();
        sb.AppendLine($"# 任务池（第 {Math.Max(1, pageNumber)} 页，共 {total} 条）");
        sb.AppendLine();

        if (rows.Count == 0)
        {
            sb.AppendLine("_（空）_");
            return sb.ToString();
        }

        sb.AppendLine("| Id | TaskKey | Pri | Retry | Status | CreatedAt | UpdatedAt |");
        sb.AppendLine("|----|---------|----:|------:|--------|-----------|-----------|");
        foreach (var r in rows)
        {
            sb.Append("| ")
              .Append(r.Id).Append(" | `").Append(r.TaskKey).Append("` | ")
              .Append(r.Priority).Append(" | ").Append(r.MaxRetries).Append(" | ")
              .Append(r.Status).Append(" | ")
              .Append(r.CreatedAt).Append(" | ")
              .Append(r.UpdatedAt).AppendLine(" |");
        }
        sb.AppendLine();

        foreach (var r in rows)
        {
            sb.Append("## `").Append(r.TaskKey).Append("`  ").AppendLine(r.Status);
            sb.AppendLine();
            sb.Append("**Id:** ").Append(r.Id)
              .Append(" · **Priority:** ").Append(r.Priority)
              .Append(" · **MaxRetries:** ").Append(r.MaxRetries).AppendLine();
            sb.AppendLine();
            sb.AppendLine("```json");
            sb.AppendLine(Pretty(r.PayloadJson));
            sb.AppendLine("```");
            sb.AppendLine();
        }
        return sb.ToString();
    }

    /// <summary>原始 PayloadJson → pretty JSON 文本（中文原样 + 缩进）；空/非法返回占位，不抛。</summary>
    public static string Pretty(string? rawJson)
    {
        if (string.IsNullOrWhiteSpace(rawJson)) return "{}";
        try
        {
            return JsonNode.Parse(rawJson)?.ToJsonString(PrettyJson) ?? "{}";
        }
        catch (JsonException)
        {
            return rawJson; // 非 JSON 载荷原样展示
        }
    }
}
