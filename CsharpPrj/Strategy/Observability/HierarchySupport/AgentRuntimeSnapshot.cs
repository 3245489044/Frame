using System;
using System.Collections.Generic;

namespace Share.HierarchySupport;

/// <summary>
/// 运行时整体状态快照（只读投影）—— 运行时模块的实时状态查询载体。
/// 由 AgentRunner 在每轮状态转移 / 运行边界把统一整体状态快照（AgentTaskItem）投影发布；
/// 外部经 <see cref="IRuntimeStateProvider"/> 查询或订阅。与数据库/持久化无关（后者见 Database 门面）。
/// </summary>
public sealed record AgentRuntimeSnapshot(
    int Round,
    string Stage,
    int BudgetLevel,
    int TokenValue,
    int TokenMax,
    bool Halt,
    int ContinuousFailures,
    string? CurrentStrategyId,
    IReadOnlyList<string> AvailableStrategyIds,
    IReadOnlyList<ExecutorRunStatus> AgentStatuses,
    IReadOnlyList<string> RecentTransitions,
    DateTimeOffset UpdatedAt)
{
    /// <summary>空 / 未运行占位（Round=0、Stage=idle）。</summary>
    public static readonly AgentRuntimeSnapshot Empty = new(
        Round: 0, Stage: "idle", BudgetLevel: 0, TokenValue: 0, TokenMax: 0,
        Halt: false, ContinuousFailures: 0, CurrentStrategyId: null,
        AvailableStrategyIds: Array.Empty<string>(),
        AgentStatuses: Array.Empty<ExecutorRunStatus>(),
        RecentTransitions: Array.Empty<string>(),
        UpdatedAt: default);
}
