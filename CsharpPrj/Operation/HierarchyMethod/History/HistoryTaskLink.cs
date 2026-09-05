namespace Operation.HierarchyMethod.History;

/// <summary>历史归拢任务的链接记录（History → Task 的 history_owns_task 引用边投影）。</summary>
public sealed record HistoryTaskLink(
    string HistoryId,
    string TaskKey);
