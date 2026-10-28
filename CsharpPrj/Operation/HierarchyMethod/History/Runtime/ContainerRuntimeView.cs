namespace Operation.HierarchyMethod.History.Runtime;

/// <summary>
/// history 容器运行态聚合视图（只读投影）—— 由该 history 归拢的任务状态派生，非独立状态源。
/// 计数在 <see cref="HistoryRuntimeService.GetOverviewAsync"/> 内对 <see cref="Tasks"/> 各状态文本聚合生成，
/// 供 history 侧一次总览多 task 运行情况。
/// </summary>
public sealed record HistoryRuntimeView(
    string HistoryId,
    /// <summary>该 history 归拢的任务总数。</summary>
    int TaskCount,
    /// <summary>激活（Active=true）任务数。</summary>
    int ActiveCount,
    /// <summary>运行中（Running）任务数。</summary>
    int RunningCount,
    /// <summary>挂起（Paused）任务数。</summary>
    int PausedCount,
    /// <summary>完成（Completed/Done）任务数。</summary>
    int DoneCount,
    /// <summary>失败（Faulted/Error）任务数。</summary>
    int ErrorCount,
    /// <summary>逐 task 状态明细。</summary>
    IReadOnlyList<HistoryTaskRunStatus> Tasks);
