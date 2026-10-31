namespace DataCenter.Protocol.Dataflow;

/// <summary>重算计划的一步（<see cref="Order"/> 升序 = 拓扑序：上游先于下游）。</summary>
/// <param name="ProductId">需重算的产品。</param>
/// <param name="Order">拓扑序位（0 起）。</param>
public sealed record RecomputeStep(string ProductId, int Order);

/// <summary>
/// 重算计划（DP6：以数据影响面为单位，不得以整链重跑冒充增量）。
/// </summary>
/// <param name="Steps">拓扑序步骤（截断时只含预算内部分）。</param>
/// <param name="IsTruncated">是否因预算截断（显式可见，禁静默）。</param>
/// <param name="Status">结果状态（<see cref="GraphQueryStatus"/>）。</param>
/// <param name="Reason">状态原因（截断/环检测等）。</param>
public sealed record RecomputePlan(
    IReadOnlyList<RecomputeStep> Steps,
    bool IsTruncated,
    string Status,
    string? Reason = null);

/// <summary>
/// 受影响面重算规划器 —— 纯函数式端口（同输入同输出，可切开关回归整链重跑）。
/// <para>
/// 闭包 = <c>Descendants(product)</c> ∩ 分区裁剪 ∩ 预算（MaxNodes/MaxDepth）；序 = 拓扑序（环即跳过并报）。
/// </para>
/// 权威: Frame\DataCenter\Doc\DataCenter-工作流数据产品图框架-设计与实施.md §3.5 / D5。
/// </summary>
public interface IRecomputePlanner
{
    /// <summary>以产品为起点的影响面闭包。</summary>
    RecomputePlan PlanFromProduct(string productId);

    /// <summary>以字段路径为起点的影响面闭包（字段级增量）。</summary>
    RecomputePlan PlanFromField(string fieldPath);

    /// <summary>幂等键（结构事实：产品 + 层 + 入字段内容指纹；不含易变分量，判据 C17）。</summary>
    string ComputeIdempotencyKey(DataProductSpec product);
}
