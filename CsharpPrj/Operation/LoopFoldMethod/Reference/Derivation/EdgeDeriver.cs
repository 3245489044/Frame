using System;
using System.Collections.Generic;
using System.Linq;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;
using Operation.LoopFoldMethod.Reference.Model;

namespace Operation.LoopFoldMethod.Reference.Derivation;

/// <summary>
/// 边推导门面（Reference 空间维度，纯推演、LLM-free）—— 收敛自 <c>HierarchyMethod.Nodes.Relations.NodeRelation</c>
/// 与 <c>WorkflowReferenceStaticizer</c>：工作流「组合 + 控制流」静态引用边（Owns / Calls）与节点关系边
/// （KeyMappingToNode / NodeToNode）的单一推导面。
/// 单一来源纪律（TH-12）：边语义推导唯一 owner 在 <see cref="NodeRelationDeriver"/>，本门面薄委托其推导并映射为
/// <see cref="ReferenceEdge"/> 中立模型，不复制边推导逻辑；只描述「实体间存在哪种引用关系」。
/// </summary>
public static class EdgeDeriver
{
    /// <summary>节点资源 Id：{workflowId}.{nodeKey}（委托 <see cref="NodeRelationFactory.NodeId"/>，不新造 Id 语义）。</summary>
    public static string NodeId(string workflowId, string nodeKey) => NodeRelationFactory.NodeId(workflowId, nodeKey);

    /// <summary>推导工作流「组合 + 控制流」静态引用边（Owns + Calls，模板粒度一次常驻），映射为中立 <see cref="ReferenceEdge"/>。</summary>
    public static IReadOnlyList<ReferenceEdge> DeriveWorkflowControlFlowEdges(string workflowId, IReadOnlyList<string> stepNodeKeys)
        => NodeRelationDeriver.DeriveWorkflowControlFlowEdges(workflowId, stepNodeKeys)
            .Select(ToEdge)
            .ToList();

    /// <summary>提取「稳定步骤键 + 控制流边」的共享骨架（单一提取点，消除 WorkflowResultDeriver / WorkflowCompiler 各自推导的重复）。</summary>
    public static (IReadOnlyList<string> Keys, IReadOnlyList<ReferenceEdge> Edges) DeriveParts(
        string workflowId, IReadOnlyList<string> stepNodeKeys)
    {
        if (string.IsNullOrWhiteSpace(workflowId) || stepNodeKeys is not { Count: > 0 })
            return (Array.Empty<string>(), Array.Empty<ReferenceEdge>());
        var keys = stepNodeKeys.ToList();
        return (keys, DeriveWorkflowControlFlowEdges(workflowId, keys));
    }

    /// <summary>推导某节点的权威节点关系边全集（KeyMappingToNode + NodeToNode），供统一写入/对账（委托 <see cref="NodeRelationDeriver"/>）。</summary>
    public static IReadOnlyList<NodeRelation> DeriveEdgesForNode(
        string workflowId, string nodeKey, bool hasMapping,
        IEnumerable<KeyValuePair<string, (string Node, string Port)>>? provenance)
        => NodeRelationDeriver.DeriveEdgesForNode(workflowId, nodeKey, hasMapping, provenance);

    /// <summary>把「映射存在性 + 溯源」序列化为节点关系状态 JSON（委托 <see cref="NodeRelationDeriver"/>）。</summary>
    public static string SerializeRelations(
        bool hasMapping, IEnumerable<KeyValuePair<string, (string Node, string Port)>>? provenance)
        => NodeRelationDeriver.SerializeRelations(hasMapping, provenance);

    /// <summary>静态引用（DataStoreDriven）→ 中立引用边（Reference）。</summary>
    private static ReferenceEdge ToEdge(StaticReference r) => new(r.RefType, r.SourceId, r.TargetId);
}
