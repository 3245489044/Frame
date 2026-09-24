using System.Collections.Generic;
using System.Linq;
using Operation.HierarchyMethod.Nodes.Workflows;

namespace Operation.HierarchyMethod.Nodes.Execution;

/// <summary>
/// 轮次产物装配（CP-1，唯一入口）：把任意 runner 的轮次输出归一为 <see cref="RoundArtifacts"/> ——
/// 堵住「折叠 runner Nodes 恒空」的分裂，并使终答归因（<see cref="RoundArtifacts.TerminalNodeKey"/>）
/// 由单点决定（入史层标记依据）。
/// <para>纯函数零 IO：只做形状归一与终答归因，不落库（落库经 <see cref="NodeStepPersistCoordinator.PersistRoundAsync"/>）。</para>
/// </summary>
public static class RoundArtifactAssembler
{
    /// <summary>装配一轮产物。</summary>
    /// <param name="taskKey">会话/任务键。</param>
    /// <param name="round">轮次号。</param>
    /// <param name="spec">本轮骨架（终答归因按步骤顺序；可为 null = 无骨架声明）。</param>
    /// <param name="nodes">本轮全量节点产出。</param>
    /// <param name="terminalText">终答文本（null = 无终答）。</param>
    /// <param name="archives">归档层条目（可选）。</param>
    public static RoundArtifacts FromRound(
        string taskKey, int round, RoundSpec? spec,
        IReadOnlyList<NodeExecutionRecord>? nodes, string? terminalText,
        IReadOnlyList<RoundArchiveItem>? archives = null)
    {
        var list = nodes ?? (IReadOnlyList<NodeExecutionRecord>)System.Array.Empty<NodeExecutionRecord>();
        return new RoundArtifacts(taskKey, round, list, ResolveTerminalNodeKey(spec, list), terminalText, archives);
    }

    /// <summary>
    /// 终答来源节点（默认标记规则①：链终点产出）：按 <paramref name="spec"/> 步骤顺序取**最后一个有序产出**的节点；
    /// 无骨架声明 → 取最后一个产出节点；无任何产出 → null（不入史，不伪造终答）。
    /// </summary>
    public static string? ResolveTerminalNodeKey(RoundSpec? spec, IReadOnlyList<NodeExecutionRecord> nodes)
    {
        if (nodes is not { Count: > 0 }) return null;
        if (spec?.Steps is not { Count: > 0 }) return LastProducedKey(nodes, null);
        for (var i = spec.Steps.Count - 1; i >= 0; i--)
        {
            var key = spec.Steps[i].EffectiveNodeId;
            if (string.IsNullOrWhiteSpace(key)) continue;
            var hit = LastProducedKey(nodes, key);
            if (hit is not null) return hit;
        }
        return LastProducedKey(nodes, null);
    }

    private static string? LastProducedKey(IReadOnlyList<NodeExecutionRecord> nodes, string? nodeKey)
    {
        for (var i = nodes.Count - 1; i >= 0; i--)
        {
            var candidate = nodes[i].NodeKey;
            if (string.IsNullOrWhiteSpace(candidate)) continue;
            if (nodeKey is null || string.Equals(candidate, nodeKey, System.StringComparison.Ordinal)) return candidate;
        }
        return null;
    }
}
