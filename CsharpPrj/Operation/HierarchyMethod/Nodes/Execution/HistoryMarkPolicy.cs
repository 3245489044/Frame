using System;
using System.Collections.Generic;
using Operation.HierarchyMethod.Nodes.Workflows;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 节点入史标记（单一权威，CP-4）。
/// <para><see cref="Terminal"/>：终答节点（链终点产出）—— 默认标记，必须入史（LLM 上下文/主对话流真源）；
/// <see cref="Numbered"/>：显式声明的中间节点（携带 Round/Seq）—— 入史，供分层气泡；
/// <see cref="None"/>：仅留全量层（任务面板），不入史、不进 LLM 上下文。</para>
/// </summary>
public enum NodeHistoryMark
{
    /// <summary>不入史（仅任务面板全量层）。</summary>
    None,

    /// <summary>编号中间节点（显式声明；入史供分层气泡）。</summary>
    Numbered,

    /// <summary>终答节点（默认标记；入史为对话真源）。</summary>
    Terminal,
}

/// <summary>
/// 入史标记策略（CP-4，唯一决定「哪些节点进历史」的**纯函数**，零 IO）：
/// 终答节点默认 <see cref="NodeHistoryMark.Terminal"/>；声明面（<see cref="RoundStep.Mark"/>）显式
/// <c>Numbered</c> 的中间节点入史；其余 <see cref="NodeHistoryMark.None"/>。
/// <para>
/// 零配置即成立：无任何声明时「必有且仅有 1 个 Terminal」（终答来源节点），使 L-B 入史层始终有唯一对话真源。
/// </para>
/// </summary>
public static class HistoryMarkPolicy
{
    /// <summary>解析某节点的入史标记。</summary>
    /// <param name="spec">本轮骨架（提供声明面；null = 无声明 → 仅默认规则）。</param>
    /// <param name="record">待判节点产出快照。</param>
    /// <param name="terminalNodeKey">终答来源节点键（由 <see cref="RoundArtifactAssembler"/> 归因）。</param>
    public static NodeHistoryMark Resolve(RoundSpec? spec, NodeExecutionRecord record, string? terminalNodeKey)
    {
        ArgumentNullException.ThrowIfNull(record);
        if (string.IsNullOrWhiteSpace(record.NodeKey)) return NodeHistoryMark.None;
        // 终答优先：链终点产出是对话真源，声明面不得把它降级为普通编号节点。
        if (!string.IsNullOrWhiteSpace(terminalNodeKey)
            && string.Equals(record.NodeKey, terminalNodeKey, StringComparison.Ordinal))
            return NodeHistoryMark.Terminal;

        var declared = ResolveDeclaredMark(spec, record.NodeKey);
        return declared ?? NodeHistoryMark.None;
    }

    /// <summary>声明面标记（<see cref="RoundStep.Mark"/>；未声明/不可识别 → null = 走默认规则）。</summary>
    private static NodeHistoryMark? ResolveDeclaredMark(RoundSpec? spec, string nodeKey)
    {
        if (spec?.Steps is not { Count: > 0 }) return null;
        foreach (var step in spec.Steps)
        {
            if (step.Mark is not { Length: > 0 } mark) continue;
            if (!string.Equals(step.EffectiveNodeId, nodeKey, StringComparison.Ordinal)) continue;
            if (Enum.TryParse<NodeHistoryMark>(mark.Trim(), ignoreCase: true, out var parsed))
                return parsed;
        }
        return null;
    }
}
