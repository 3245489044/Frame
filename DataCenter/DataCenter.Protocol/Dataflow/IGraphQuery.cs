namespace DataCenter.Protocol.Dataflow;

/// <summary>查询结果状态码（单源；禁各处手写字符串）。</summary>
public static class GraphQueryStatus
{
    /// <summary>正常返回。</summary>
    public const string Ok = "ok";

    /// <summary>被显式跳过（如遇环），不抛异常。</summary>
    public const string Skipped = "skipped";

    /// <summary>无记录 ⇒ 不可判定（≠「不存在」，对齐 EmptyResultGate）。</summary>
    public const string Undetermined = "undetermined";

    /// <summary>到界截断（预算强制；显式可见，禁静默返回空）。</summary>
    public const string Truncated = "truncated";
}

/// <summary>子图查询结果（Q1）。</summary>
public sealed record SubgraphResult(
    IReadOnlyList<string> ProductIds,
    IReadOnlyList<DataDependencyEdge> Edges,
    bool IsTruncated,
    string Status,
    string? Reason = null);

/// <summary>归属遍历结果（Q2：祖先 / 后代）。</summary>
public sealed record HierarchyWalkResult(
    IReadOnlyList<string> ProductIds,
    bool IsTruncated,
    string Status,
    string? Reason = null);

/// <summary>字段级血缘链（Q3：一条 from→to 链接）。</summary>
public sealed record FieldLineageLink(
    string FromFieldPath,
    string ToFieldPath,
    string EdgeId,
    DependencyAssertion Assertion);

/// <summary>字段级血缘结果（Q3：三档断言随链接携带）。</summary>
public sealed record FieldLineageResult(
    IReadOnlyList<FieldLineageLink> Links,
    string Status,
    string? Reason = null);

/// <summary>影响面结果（Q4：与重算计划同源）。</summary>
public sealed record ImpactResult(
    IReadOnlyList<string> ProductIds,
    bool IsTruncated,
    string Status,
    string? Reason = null);

/// <summary>跨代差异结果（Q5：两代不可比时显式报不可比）。</summary>
public sealed record GraphDiffResult(
    int FromGeneration,
    int ToGeneration,
    IReadOnlyList<string> Added,
    IReadOnlyList<string> Removed,
    IReadOnlyList<string> Changed,
    string Status,
    string? Reason = null);

/// <summary>追溯查询结果（Q6：谁 / 何时 / 因何）。</summary>
public sealed record ProvenanceResult(
    string ProductId,
    ProvenanceRecord? Record,
    string Status,
    string? Reason = null);

/// <summary>
/// 图查询面（六问）—— 全部只读；版本源接图成员轴（<c>GraphMembership</c>）。
/// <para>
/// 纪律：影响面（Q4）与重算计划<b>同源实现</b>（Strategy.SingleImplementation），禁另算一份；
/// 遇环返回 <see cref="GraphQueryStatus.Skipped"/> 而非抛异常。
/// </para>
/// 权威: Frame\DataCenter\Doc\DataCenter-工作流数据产品图框架-设计与实施.md §6.1。
/// </summary>
public interface IGraphQuery
{
    /// <summary>Q1 按层取子图（<paramref name="maxDepth"/>=0 ⇒ 用模式预算）。</summary>
    SubgraphResult Subgraph(string? levelId = null, int maxDepth = 0);

    /// <summary>Q2 归属树上溯。</summary>
    HierarchyWalkResult Ancestors(string productId);

    /// <summary>Q2 归属树下溯。</summary>
    HierarchyWalkResult Descendants(string productId);

    /// <summary>Q4 受影响面（产品键或字段路径）。</summary>
    ImpactResult Impact(string productIdOrFieldPath);

    /// <summary>Q3 字段级血缘（无记录 ⇒ Undetermined）。</summary>
    FieldLineageResult FieldLineage(string fieldPath);

    /// <summary>Q5 跨代差异。</summary>
    GraphDiffResult Diff(int fromGeneration, int toGeneration);

    /// <summary>Q6 追溯（四要素齐备率由消费方抽样核算）。</summary>
    ProvenanceResult Provenance(string productId);
}
