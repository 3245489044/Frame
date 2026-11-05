using Modules.Node.Diagnostics;
using Modules.Node.Identity;

namespace Modules.Node.Relations;

/// <summary>
/// 溯源边推导（G2 数据流边单源）：<c>portKey(upstreamNode.port) → 下游</c> 生成 <see cref="ReferenceTypes.NodeToNode"/> 边。
/// <para>
/// 判据：上游地址无法解析 ⇒ 诊断 <c>edge.endpoint-unresolved</c> 并跳过该边（缺口可见化，不静默丢）；
/// 同边重复出现 ⇒ 幂等去重（边是关系而非事实记录）。
/// </para>
/// </summary>
public sealed class ProvenanceEdgeDeriver : IEdgeDerivation
{
    private readonly INodeDiagnosticsSink _diagnostics;

    public ProvenanceEdgeDeriver(INodeDiagnosticsSink? diagnostics = null)
        => _diagnostics = diagnostics ?? NullDiagnosticsSink.Instance;

    /// <inheritdoc />
    public IReadOnlyList<NodeEdge> Derive(EdgeDerivationInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        ArgumentNullException.ThrowIfNull(input.Provenance);

        var edges = new Dictionary<string, NodeEdge>(StringComparer.Ordinal);
        foreach (var pair in input.Provenance)
        {
            var portKey = pair.Key ?? string.Empty;
            var upstreamKey = pair.Value ?? string.Empty;
            if (portKey.Length == 0 || upstreamKey.Length == 0)
            {
                ReportUnresolved(portKey, "端口键或上游键为空");
                continue;
            }

            var separator = portKey.LastIndexOf('.');
            if (separator <= 0 || separator >= portKey.Length - 1)
            {
                ReportUnresolved(portKey, "端口键不含 {node}.{port} 形状");
                continue;
            }

            var upstream = input.ResolveUpstream?.Invoke(upstreamKey)
                           ?? NodeAddress.FromLegacy(input.Downstream.DomainKey, upstreamKey);
            if (string.IsNullOrWhiteSpace(upstream.NodeKey))
            {
                ReportUnresolved(portKey, "上游地址未解析（缺逻辑节点键）");
                continue;
            }

            var edge = new NodeEdge(
                NodeEdgeEndpoint.Of(upstream),
                NodeEdgeEndpoint.Of(input.Downstream),
                ReferenceTypes.NodeToNode,
                portKey[(separator + 1)..],
                null);

            edges[edge.EdgeKey] = edge;
        }

        return edges.Values.OrderBy(static edge => edge.EdgeKey, StringComparer.Ordinal).ToArray();
    }

    private void ReportUnresolved(string subject, string reason)
        => _diagnostics.Report(new NodeDiagnostic(
            "edge.endpoint-unresolved",
            NodeDiagnosticSeverity.Warning,
            $"溯源边端点不可解析：{reason}（该边跳过，不静默）",
            subject));
}
