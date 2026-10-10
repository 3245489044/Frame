using System;
using System.Collections.Generic;
using System.Linq;
using Modules.Node.Relations;
using NodeRelationState = Modules.Node.Relations.NodeRelationDeriver.NodeRelationState;
using ProvenanceRef = Modules.Node.Relations.NodeRelationDeriver.ProvenanceRef;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 节点关系状态 JSON 编解码（L4 伴生；B1 二批）。
/// <para>
/// 节点关系语义（<see cref="NodeRelation"/> / <see cref="NodeRelationType"/>）与关系边**纯推导**
/// （<see cref="NodeRelationDeriver"/>：DeriveEdgesForNode / DeriveWorkflowControlFlowEdges 等）
/// 已上移 L2 通用节点框架 <c>Modules.Node.Relations</c>；
/// 依赖本层序列化设施（PersistenceJson / Plate.JsonMethod）的**序列化 / 反解**两法留本层（JSON 不进框架）。
/// </para>
/// <para>
/// 关系状态 DTO（<c>NodeRelationState</c> / <c>ProvenanceRef</c>）纯 POCO 随推导器上移 L2，本类经别名引用。
/// </para>
/// </summary>
public static class RelationStateCodec
{
    /// <summary>把「映射存在性 + 溯源」序列化为节点关系状态 JSON（持久化到 NodeRecord.RelationsJson）。</summary>
    public static string SerializeRelations(bool hasMapping,
        IEnumerable<KeyValuePair<string, (string Node, string Port)>>? provenance)
    {
        var prov = new Dictionary<string, ProvenanceRef>(StringComparer.Ordinal);
        if (provenance != null)
            foreach (var kv in provenance)
                prov[kv.Key] = new ProvenanceRef { Node = kv.Value.Node, Port = kv.Value.Port };
        return PersistenceJson.Serialize(new NodeRelationState { HasMapping = hasMapping, Provenance = prov });
    }

    /// <summary>从持久化的节点关系状态 JSON 重推导权威边全集；空/非法 JSON → 空集（对账即收敛为空）。</summary>
    public static IReadOnlyList<NodeRelation> DeriveEdgesFromRelationsJson(string workflowId, string nodeKey, string? relationsJson)
    {
        if (string.IsNullOrWhiteSpace(relationsJson)) return Array.Empty<NodeRelation>();
        var state = PersistenceJson.TryDeserialize<NodeRelationState>(relationsJson);
        if (state is null) return Array.Empty<NodeRelation>();
        var provenance = state.Provenance
            .Select(kv => new KeyValuePair<string, (string Node, string Port)>(kv.Key, (kv.Value.Node, kv.Value.Port)));
        return NodeRelationDeriver.DeriveEdgesForNode(workflowId, nodeKey, state.HasMapping, provenance);
    }
}
