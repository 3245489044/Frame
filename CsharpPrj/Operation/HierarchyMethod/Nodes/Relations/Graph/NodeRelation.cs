using System;
using System.Collections.Generic;
using Operation.HierarchyMethod.Nodes;
using Operation.DataStoreDriven.Core;
using StaticReference = Operation.DataStoreDriven.Abstractions.StaticReference;
using NodeRelationFactory = Operation.DataStoreDriven.Abstractions.NodeRelationFactory;

namespace Operation.HierarchyMethod.Nodes.Relations;

/// <summary>
/// 节点关系推导门面（Phase 3 收敛）—— 把遗留共享库 <see cref="NodeRelationDeriver"/> 的关系推导能力收敛进
/// <c>Agent.Reference</c> 单一管理面。仅门面封装、不物理删 Operation 原实现（可能被他处使用），
/// 内部薄委托 <see cref="NodeRelationDeriver"/> / <see cref="NodeRelationFactory"/>。
/// <para>消费方：<see cref="WorkflowResultDeriver"/> / <see cref="WorkflowReferenceStaticizer"/> / <see cref="NodeExecutionService"/>
/// 不再直接依赖 Operation，统一经本面访问 → 节点关系逻辑只在引用域一个面。
/// 本类为<b>静态门面</b>：全部消费方均使用静态入口（<see cref="NodeId(string,string)"/> / <see cref="DeriveEdgesForNode"/> /
/// <see cref="SerializeRelations"/> / <see cref="DeriveWorkflowControlFlowEdges(string,IReadOnlyList{string})"/>），无实例态。</para>
/// </summary>
public static class NodeRelation
{
    /// <summary>节点资源 Id：{workflowId}.{nodeKey}（复用 <see cref="NodeRelationFactory.NodeId"/>，不新造 Id 语义）。</summary>
    public static string NodeId(string workflowId, string nodeKey) => NodeRelationFactory.NodeId(workflowId, nodeKey);

    /// <summary>推导某节点的权威边全集（KeyMappingToNode + NodeToNode），供统一写入/对账。</summary>
    public static IReadOnlyList<global::Operation.DataStoreDriven.Abstractions.NodeRelation> DeriveEdgesForNode(
        string workflowId, string nodeKey, bool hasMapping,
        IEnumerable<KeyValuePair<string, (string Node, string Port)>>? provenance)
        => NodeRelationDeriver.DeriveEdgesForNode(workflowId, nodeKey, hasMapping, provenance);

    /// <summary>把「映射存在性 + 溯源」序列化为节点关系状态 JSON（持久化到 <see cref="NodeRecord.RelationsJson"/>）。</summary>
    public static string SerializeRelations(
        bool hasMapping, IEnumerable<KeyValuePair<string, (string Node, string Port)>>? provenance)
        => NodeRelationDeriver.SerializeRelations(hasMapping, provenance);

    /// <summary>推导工作流「组合 + 控制流」静态引用边（Owns + Calls，模板粒度一次常驻）。</summary>
    public static IReadOnlyList<StaticReference> DeriveWorkflowControlFlowEdges(
        string workflowId, IReadOnlyList<string> stepNodeKeys)
        => NodeRelationDeriver.DeriveWorkflowControlFlowEdges(workflowId, stepNodeKeys);
}
