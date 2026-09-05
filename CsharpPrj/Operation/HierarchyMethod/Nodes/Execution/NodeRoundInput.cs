using System.Collections.Generic;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 一轮执行的输入上下文（History 层从累积历史重建、交给投影器填 L0/L1/L2 的数据形态）。
/// 由 <see cref="History.IHistoryProjector.BuildContextAsync"/> 产出，作为「上下文组装」的中立终点。
/// </summary>
public sealed record NodeRoundInput(
    string TaskKey,
    string? SessionId,
    int Round,
    IReadOnlyDictionary<string, object?> Vars);
