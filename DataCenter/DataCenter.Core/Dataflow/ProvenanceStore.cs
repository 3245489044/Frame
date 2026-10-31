using DataCenter.Protocol.Dataflow;

namespace DataCenter.Core.Dataflow;

/// <summary>
/// provenance 存储实现（D3）—— 追加式、进程内、读多写一。
/// <para>
/// 纪律：① 盖章唯一落点（<see cref="Record"/> 内部生成 <see cref="TraceStamp"/>，调用方不可自造 seq）；
/// ② 观测边断言恒为 <see cref="DependencyAssertion.Observed"/>（域侧传错即纠正为观测强度，禁冒充 declared）；
/// ③ 只接受数据依赖边族（<see cref="DataEdgeKinds.Derives"/> / <see cref="DataEdgeKinds.Aggregates"/>），
/// 归属/顺序边不得伪装成观测（DP5）。
/// </para>
/// 权威: Frame\DataCenter\Doc\DataCenter-工作流数据产品图框架-设计与实施.md §3.6 / D3。
/// </summary>
public sealed class ProvenanceStore : IProvenanceStore
{
    private static readonly HashSet<string> ObservedKinds =
        new(StringComparer.Ordinal) { DataEdgeKinds.Derives, DataEdgeKinds.Aggregates };

    private readonly object _gate = new();
    private readonly List<ProvenanceRecord> _records = [];
    private long _seq;

    /// <summary>盖章并登记一次写入（同一次调用内 seq 单调递增）。</summary>
    public void Record(
        string productId,
        string writer,
        string? causedBy,
        int chainDepth,
        IReadOnlyList<DataDependencyEdge> observedEdges)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(productId);
        ArgumentException.ThrowIfNullOrWhiteSpace(writer);
        ArgumentNullException.ThrowIfNull(observedEdges);
        if (chainDepth < 0)
            throw new ArgumentOutOfRangeException(nameof(chainDepth), chainDepth, "因果链深度不得为负。");

        var normalized = new DataDependencyEdge[observedEdges.Count];
        for (var i = 0; i < observedEdges.Count; i++)
        {
            var edge = observedEdges[i] ?? throw new ArgumentException("观测边集合含 null 项。", nameof(observedEdges));
            if (!ObservedKinds.Contains(edge.EdgeKind))
                throw new ArgumentException(
                    $"观测边只允许 {DataEdgeKinds.Derives} / {DataEdgeKinds.Aggregates}：{edge.EdgeKind}（归属与顺序边不得记为观测）。",
                    nameof(observedEdges));
            normalized[i] = edge with { Assertion = DependencyAssertion.Observed };
        }

        lock (_gate)
        {
            var stamp = new TraceStamp(writer.Trim(), DateTimeOffset.UtcNow, ++_seq);
            _records.Add(new ProvenanceRecord(productId.Trim(), stamp, causedBy, chainDepth, normalized));
        }
    }

    public IReadOnlyList<ProvenanceRecord> Get(string? productId = null)
    {
        lock (_gate)
        {
            return _records
                .Where(r => productId is null || string.Equals(r.ProductId, productId.Trim(), StringComparison.Ordinal))
                .OrderBy(r => r.Stamp?.Seq ?? 0)
                .ToArray();
        }
    }

    public IReadOnlyList<DataDependencyEdge> ObservedEdges(string? productId = null)
    {
        lock (_gate)
        {
            return _records
                .Where(r => productId is null || string.Equals(r.ProductId, productId.Trim(), StringComparison.Ordinal))
                .SelectMany(r => r.ObservedEdges)
                .GroupBy(e => e.EdgeId, StringComparer.Ordinal)
                .Select(g => g.Last())
                .OrderBy(e => e.EdgeId, StringComparer.Ordinal)
                .ToArray();
        }
    }
}
