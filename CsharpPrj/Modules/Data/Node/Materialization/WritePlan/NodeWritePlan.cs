namespace Modules.Node.Materialization.WritePlan;

/// <summary>计划短路：把「前提已满足 → 跳过该步」显式声明为数据（被短路步视为已满足，下游继续）。</summary>
/// <param name="WorkId">被短路的步骤 Id。</param>
/// <param name="Reason">短路原因（可审计）。</param>
public sealed record NodeWriteShortCircuit(string WorkId, string Reason);

/// <summary>
/// 写计划（唯一编排点的产物）：有序步骤 + 显式短路声明。纯数据——可打印、可对账、可测试。
/// </summary>
/// <param name="Id">计划 Id。</param>
/// <param name="Steps">步骤集（执行前按依赖稳定拓扑排序）。</param>
/// <param name="ShortCircuits">短路声明（可空）。</param>
public sealed record NodeWritePlan(
    string Id,
    IReadOnlyList<NodeWriteWork> Steps,
    IReadOnlyList<NodeWriteShortCircuit>? ShortCircuits = null)
{
    /// <summary>短路声明集（去 null，空集非 null）。</summary>
    public IReadOnlyList<NodeWriteShortCircuit> Skips => ShortCircuits ?? Array.Empty<NodeWriteShortCircuit>();
}
