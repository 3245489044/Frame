using System.Collections.Generic;
using Share.StateManagement;

namespace Share.AiScheduling;

/// <summary>
/// 导演决策输入：整体状态的只读投影（契约纯化，全部以字符串/只读集合承载）。
/// 由状态机从统一快照构造：当前策略 + 可用策略集 + 快照遥测 + 运行中执行状态 + 近期转移历史。
/// <see cref="HistoryContext"/> 为可选的标准上下文精简文本（history 主循环视图的已渲染摘要）——让导演在决策时可参考对话历史；
/// 非 history 调度（AgentRunner 等）不注入，保持 null 不变更行为。渲染职责留在 Agent 层（通用调度骨架不绑定会话类型）。
/// </summary>
public sealed record SchedulerDirectorContext(
    string? CurrentStrategyId,
    IReadOnlyList<string> AvailableStrategyIds,
    SnapshotTelemetry Telemetry,
    IReadOnlyDictionary<string, string> AgentStatus,
    IReadOnlyList<string> RecentTransitions,
    string? HistoryContext = null);
