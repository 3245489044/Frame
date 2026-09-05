using System;
using System.Collections.Generic;
using System.Linq;

namespace Operation.LoopFoldMethod.Reference.Projection;

/// <summary>
/// 折叠头投影在空间维度的默认实现（RefHead）—— 把累积历史（History→Task→Node 归拢的节点 + round_sequence 时序边）
/// 折叠成「此刻的判定视图」。思想对齐 <c>IFoldHeadProjector.Head(trace)</c>（前缀收敛 → 当前判定点），作用于引用图而非状态带。
/// <para>
/// 折叠规则（确定性、LLM-free）：沿 round_sequence 时序边做链式拓扑折叠 —— 入度为 0 的节点（链头）起，沿后继链推到底，
/// 得到当前视图的时序序列；孤立/成环节点按 NodeKeys 顺序追加兜底。NodeKeys 为归拢节点集，Sequence 为时序序。
/// </para>
/// <para>
/// 本默认投影**只读**累积引用关系（来自编排门面聚合的 <see cref="RefHeadContext"/>），不写边（写边唯一 owner 在 DataStoreDriven）。
/// LLM 丰富分层提示词视图由 Agent/Planner 实现 <see cref="IRefProjector{TContext}"/> 提供，不在本层。
/// </para>
/// </summary>
public sealed class RefHead : IRefProjector<RefHeadContext>
{
    /// <summary>共享默认实例（无状态纯投影）。</summary>
    public static readonly RefHead Default = new();

    /// <inheritdoc />
    public IRefView Project(RefHeadContext ctx)
    {
        ArgumentNullException.ThrowIfNull(ctx);

        var nodeKeys = ctx.NodeKeys ?? Array.Empty<string>();
        var nodeSet = new HashSet<string>(nodeKeys, StringComparer.Ordinal);

        // 端点并入节点集（时序边端点也属该作用域挂载的节点）。
        var successor = new Dictionary<string, string>(StringComparer.Ordinal);
        var inDegree = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var (from, to) in ctx.RoundSequence ?? Enumerable.Empty<(string, string)>())
        {
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) continue;
            nodeSet.Add(from);
            nodeSet.Add(to);
            successor.TryAdd(from, to);   // 链式（round_sequence 为线性序）：首个后继为准，避免重复边扰动
            if (!inDegree.ContainsKey(from)) inDegree[from] = 0;
            inDegree[to] = inDegree.GetValueOrDefault(to) + 1;
        }

        var sequence = new List<string>();
        var visited = new HashSet<string>(StringComparer.Ordinal);

        // 链头（入度 0）按 NodeKeys 序取；若全部入度>0（成环），从 NodeKeys 首个未访问节点起兜底。
        var heads = nodeKeys.Where(n => inDegree.GetValueOrDefault(n) == 0).ToList();
        if (heads.Count == 0 && nodeKeys.Count > 0)
            heads.Add(nodeKeys[0]);

        foreach (var head in heads)
        {
            if (visited.Contains(head)) continue;
            var cursor = head;
            while (!string.IsNullOrEmpty(cursor) && visited.Add(cursor))
            {
                sequence.Add(cursor);
                cursor = successor.GetValueOrDefault(cursor) ?? string.Empty;
            }
        }

        // 兜底：孤立/成环未遍历的节点，按 NodeKeys 顺序追加。
        foreach (var node in nodeKeys)
            if (!visited.Contains(node))
                sequence.Add(node);
        // 端点（非 NodeKeys 成员）兜底，按序追加。
        foreach (var node in nodeSet)
            if (!visited.Contains(node))
                sequence.Add(node);

        return new RefView(ctx.ScopeKey, nodeKeys.ToList(), sequence);
    }
}
