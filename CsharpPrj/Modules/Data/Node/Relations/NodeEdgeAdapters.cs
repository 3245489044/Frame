using Modules.Node.Instances;

namespace Modules.Node.Relations;

/// <summary>
/// 关系统一视图的**兼容适配器**：<see cref="NodeRelation"/> / <see cref="InstanceEdge"/> ↔ <see cref="NodeEdge"/>。
/// <para>
/// 纪律：适配只做形状映射，不判语义、不写库；未知类别一律返回 <c>null</c>（调用方可见化，不静默降级）。
/// </para>
/// </summary>
public static class NodeEdgeAdapters
{
    /// <summary>既有关系 → 统一边（端点类别取资源类型；两条关系类别都映射为同一形状）。</summary>
    public static NodeEdge FromRelation(NodeRelation relation)
    {
        ArgumentNullException.ThrowIfNull(relation);
        return new NodeEdge(
            new NodeEdgeEndpoint(relation.SourceResourceType, relation.SourceId),
            string.IsNullOrEmpty(relation.TargetId)
                ? null
                : new NodeEdgeEndpoint(relation.TargetResourceType, relation.TargetId),
            relation.Type == NodeRelationType.KeyMappingToNode
                ? ReferenceTypes.KeyMappingToNode
                : ReferenceTypes.NodeToNode,
            relation.SourcePort,
            relation.TargetPort);
    }

    /// <summary>统一边 → 既有关系（只支持两类节点关系；其它类别 ⇒ <c>null</c>）。</summary>
    public static NodeRelation? ToRelation(NodeEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        if (edge.To is null)
        {
            return null;
        }

        var type = edge.Kind switch
        {
            ReferenceTypes.KeyMappingToNode => NodeRelationType.KeyMappingToNode,
            ReferenceTypes.NodeToNode => NodeRelationType.NodeToNode,
            _ => (NodeRelationType?)null,
        };

        if (type is null)
        {
            return null;
        }

        return new NodeRelation
        {
            Type = type.Value,
            SourceResourceType = edge.From.Kind,
            SourceId = edge.From.Id,
            TargetResourceType = edge.To.Kind,
            TargetId = edge.To.Id,
            SourcePort = edge.FromPort,
            TargetPort = edge.ToPort,
        };
    }

    /// <summary>跨实例边 → 统一边（端点为实例文本键；类别用 <c>Owns/Link/Ref</c>）。</summary>
    public static NodeEdge FromInstanceEdge(InstanceEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        return new NodeEdge(
            new NodeEdgeEndpoint(edge.From.Kind, edge.From.ToString()),
            new NodeEdgeEndpoint(edge.To.Kind, edge.To.ToString()),
            EdgeKindOf(edge.Kind));
    }

    /// <summary>统一边 → 跨实例边（非实例端点 / 无法反解 ⇒ <c>null</c>）。</summary>
    public static InstanceEdge? ToInstanceEdge(NodeEdge edge)
    {
        ArgumentNullException.ThrowIfNull(edge);
        if (edge.To is null ||
            !InstanceId.TryParse(edge.From.Id, out var from) ||
            !InstanceId.TryParse(edge.To.Id, out var to) ||
            !TryInstanceEdgeKind(edge.Kind, out var kind))
        {
            return null;
        }

        return new InstanceEdge(from, to, kind);
    }

    private static string EdgeKindOf(InstanceEdgeKind kind) => kind switch
    {
        InstanceEdgeKind.Owns => ReferenceTypes.Owns,
        InstanceEdgeKind.Link => nameof(InstanceEdgeKind.Link),
        InstanceEdgeKind.Ref => nameof(InstanceEdgeKind.Ref),
        _ => nameof(InstanceEdgeKind.Link),
    };

    private static bool TryInstanceEdgeKind(string kind, out InstanceEdgeKind value)
    {
        switch (kind)
        {
            case ReferenceTypes.Owns:
                value = InstanceEdgeKind.Owns;
                return true;
            case nameof(InstanceEdgeKind.Link):
                value = InstanceEdgeKind.Link;
                return true;
            case nameof(InstanceEdgeKind.Ref):
                value = InstanceEdgeKind.Ref;
                return true;
            default:
                value = default;
                return false;
        }
    }
}
