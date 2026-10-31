namespace DataCenter.Protocol.Dataflow;

/// <summary>
/// 一次写入的追溯记录（provenance 与 lineage 同图不同族：本记录是「谁/何时/因何」，
/// 而非「数据去了哪」）。
/// </summary>
/// <param name="ProductId">被写入的产品。</param>
/// <param name="Stamp">盖章（writer/at/seq）；null = 未盖章（不可信记录）。</param>
/// <param name="CausedBy">因果来源（域侧显式传入；Core 不推断）。</param>
/// <param name="ChainDepth">因果链深度（&gt;= 0）。</param>
/// <param name="ObservedEdges">本次写入实测的数据依赖边（断言强度恒 <see cref="DependencyAssertion.Observed"/>）。</param>
public sealed record ProvenanceRecord(
    string ProductId,
    TraceStamp? Stamp,
    string? CausedBy,
    int ChainDepth,
    IReadOnlyList<DataDependencyEdge> ObservedEdges)
{
    /// <summary>四要素齐备（writer / at / seq / causedBy；K7 判据载体）。</summary>
    public bool IsComplete
        => Stamp is { } stamp
           && stamp.IsComplete
           && !string.IsNullOrWhiteSpace(CausedBy)
           && ChainDepth >= 0;
}

/// <summary>
/// 运行期 provenance 端口（读多写一）—— 唯一写入口 <see cref="Record"/>，与域写同事务调用。
/// <para>
/// 纪律：本端口只记录<b>实际发生</b>的依赖（<c>observed</c>），声明面依赖走 schema（<c>declared</c>），
/// 不可判定依赖走 <c>may</c>；三档不得混用（DP4）。
/// </para>
/// 权威: Frame\DataCenter\Doc\DataCenter-工作流数据产品图框架-设计与实施.md §3.6 / D3。
/// </summary>
public interface IProvenanceStore
{
    /// <summary>盖章并登记一次写入（边断言恒为 observed；非法边种类/越权端点 ⇒ 抛）。</summary>
    void Record(
        string productId,
        string writer,
        string? causedBy,
        int chainDepth,
        IReadOnlyList<DataDependencyEdge> observedEdges);

    /// <summary>取追溯记录（<paramref name="productId"/> 为 null = 全部；按 seq 升序）。</summary>
    IReadOnlyList<ProvenanceRecord> Get(string? productId = null);

    /// <summary>取观测边（<paramref name="productId"/> 为 null = 全部；按 EdgeId 升序去重）。</summary>
    IReadOnlyList<DataDependencyEdge> ObservedEdges(string? productId = null);
}
