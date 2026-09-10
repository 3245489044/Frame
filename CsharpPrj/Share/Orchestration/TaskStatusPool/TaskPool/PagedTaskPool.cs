using System.Collections.Generic;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>任务池分页读取结果：一页条目 + 该范围总条数（供分页 UI 计算总页数）。</summary>
public sealed record PagedTaskPool<TItem>(IReadOnlyList<TItem> Items, int Total);

/// <summary>
/// ur_tasks 原始行视图（浏览/导出用）：含自增 Id、时间戳与原始 PayloadJson 串，
/// 供分页 UI / md 渲染器展示全字段（含 pretty 化载荷）。
/// </summary>
public sealed record TaskPoolRowView(
    long Id,
    string TaskKey,
    int Priority,
    int MaxRetries,
    string Status,
    string? PayloadJson,
    string? CreatedAt,
    string? UpdatedAt);
