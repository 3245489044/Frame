namespace Modules.Node.Relations;

/// <summary>
/// 节点关系边推导器（唯一边推导器，R1：节点关系边 = project(映射, 溯源)）。
/// 从「映射存在性 + 溯源（参数键 ← 上游节点输出端口）」推导 <see cref="NodeRelationType.KeyMappingToNode"/>
/// 与 <see cref="NodeRelationType.NodeToNode"/> 边，复用 <see cref="NodeRelationFactory"/> 构造端点 Id（不新造 Id 语义）。
/// 业务/执行代码只提供「来源」（映射 / 溯源），不手写边（对齐公理 DerivedDataAutoMaintenance）。
/// 消费方：上层执行服务在节点执行后经本推导器产出边，再由引用守卫统一写入；
/// 关系状态 JSON 的序列化/反解依赖宿主序列化设施，由上层伴生编解码类承载（框架不持 JSON 序列化）。
/// </summary>
public static class NodeRelationDeriver
{
    /// <summary>
    /// 节点关系状态（边的权威来源）：「映射存在性 + 溯源」。
    /// 随业务节点记录持久化（节点记录的 RelationsJson 字段），供对账在任意修改/全量同步时重推导权威边，无需重跑执行。
    /// 溯源用 <see cref="ProvenanceRef"/> DTO 承载（System.Text.Json 可稳定往返，不用 ValueTuple）。
    /// </summary>
    public sealed class NodeRelationState
    {
        /// <summary>节点是否有任一 Input/OutputMapping（决定 KeyMappingToNode 边）。</summary>
        public bool HasMapping { get; init; }

        /// <summary>溯源：参数键 → (源节点, 源端口)（决定 NodeToNode 边）。</summary>
        public Dictionary<string, ProvenanceRef> Provenance { get; init; } = new(StringComparer.Ordinal);
    }

    /// <summary>溯源引用 DTO（可 JSON 往返的源节点 + 源端口）。</summary>
    public sealed class ProvenanceRef
    {
        public string Node { get; init; } = string.Empty;

        public string Port { get; init; } = string.Empty;
    }

    /// <summary>
    /// 推导 KeyMapping↔节点 边：节点有任一 InputMapping/OutputMapping 时，keymapping 资源 → 节点资源。
    /// </summary>
    public static NodeRelation DeriveKeyMappingEdge(string workflowId, string nodeKey)
        => NodeRelationFactory.KeyMappingToNode(workflowId, nodeKey, nodeKey);

    /// <summary>
    /// 依溯源逐条推导 NodeToNode 数据流组合边（上游节点输出端口 → 下游节点输入端口）。
    /// <paramref name="provenance"/> 为「参数键 ← (源节点, 源端口)」映射（执行期溯源的通用投影）。
    /// </summary>
    public static IEnumerable<NodeRelation> DeriveNodeToNodeEdges(
        string workflowId, string toNode,
        IEnumerable<KeyValuePair<string, (string Node, string Port)>> provenance)
    {
        if (provenance is null) yield break;
        foreach (var kv in provenance)
        {
            var (srcNode, srcPort) = kv.Value;
            if (string.IsNullOrWhiteSpace(srcNode)) continue;
            yield return NodeRelationFactory.NodeToNode(workflowId, srcNode, srcPort, toNode, kv.Key);
        }
    }

    /// <summary>推导某节点的权威边全集（KeyMappingToNode + NodeToNode），供统一写入/对账。</summary>
    public static IReadOnlyList<NodeRelation> DeriveEdgesForNode(
        string workflowId, string nodeKey, bool hasMapping,
        IEnumerable<KeyValuePair<string, (string Node, string Port)>>? provenance)
    {
        var edges = new List<NodeRelation>();
        if (hasMapping)
            edges.Add(DeriveKeyMappingEdge(workflowId, nodeKey));
        edges.AddRange(DeriveNodeToNodeEdges(workflowId, nodeKey, provenance));
        return edges;
    }

    /// <summary>声明接线（G3）：上游节点/端口 → 下游节点/端口（构建期由工作流 <c>inports/outports</c> 声明投影）。</summary>
    public sealed record DeclaredPortWire(
        string FromNode, string FromPort, string ToNode, string ToPort, string? KeyMappingRef = null);

    /// <summary>
    /// G3 声明式数据流边（**构建期**唯一来源）：由工作流 <c>[Node] inports/outports</c> 接线在构建期确定
    /// <see cref="NodeRelationType.NodeToNode"/> 边，取代运行期从 <c>RelationsJson</c> 推导。
    /// 声明面就绪后本方法为唯一构建源；<see cref="DeriveNodeToNodeEdges"/> 降为执行期兜底。
    /// </summary>
    public static IEnumerable<NodeRelation> DeriveDeclaredDataflowEdges(
        string workflowId, IEnumerable<DeclaredPortWire> wiring)
    {
        if (wiring is null) yield break;
        foreach (var w in wiring)
        {
            if (w is null) continue;
            if (string.IsNullOrWhiteSpace(w.FromNode) || string.IsNullOrWhiteSpace(w.ToNode)) continue;
            yield return NodeRelationFactory.NodeToNode(workflowId, w.FromNode, w.FromPort, w.ToNode, w.ToPort, w.KeyMappingRef);
        }
    }

    /// <summary>从一条声明关系推导边（G3 主动声明；声明即权威，图库永不比 SQL 多出未声明关系）。仅 active 声明产生边。
    /// 端点 Id 以声明记录的 ResourceType + Id 为准（预声明可指向尚未执行的节点，不依赖执行溯源）。</summary>
    public static NodeRelation? DeriveEdgeFromDeclaration(string workflowId, RelationDeclaration declaration)
    {
        if (declaration is null || !declaration.IsActive) return null;
        var source = declaration.Source ?? new RelationEndpoint();
        var target = declaration.Target ?? new RelationEndpoint();
        if (string.IsNullOrWhiteSpace(source.Id) || string.IsNullOrWhiteSpace(target.Id)) return null;

        var relationType = ResolveRelationType(declaration.RefType);
        return new NodeRelation
        {
            Type = relationType,
            SourceResourceType = string.IsNullOrWhiteSpace(source.ResourceType) ? NodeResourceTypes.Node : source.ResourceType,
            SourceId = source.Id,
            SourcePort = source.Port,
            TargetResourceType = string.IsNullOrWhiteSpace(target.ResourceType) ? NodeResourceTypes.Node : target.ResourceType,
            TargetId = target.Id,
            TargetPort = target.Port,
            KeyMappingRef = declaration.KeyMappingRef,
            Strength = 1.0,
        };
    }

    /// <summary>从一批声明记录推导边全集（过滤 active + 空端点）。</summary>
    public static IReadOnlyList<NodeRelation> DeriveEdgesFromDeclarations(string workflowId,
        IEnumerable<RelationDeclaration>? declarations)
    {
        if (declarations is null) return Array.Empty<NodeRelation>();
        var edges = new List<NodeRelation>();
        foreach (var d in declarations)
        {
            if (d is null) continue;
            var edge = DeriveEdgeFromDeclaration(workflowId, d);
            if (edge is not null) edges.Add(edge);
        }
        return edges;
    }

    /// <summary>
    /// 推导工作流「组合 + 控制流」静态引用边（模板粒度，一次推导即常驻）：
    ///  · <c>Owns</c>：workflow 资源（Id=workflowId）→ 每个步骤节点（组合：工作流包含步骤）
    ///  · <c>Calls</c>：步骤 i → 步骤 i+1（控制流：上一步骤执行后进入下一步骤，依 RoundSpec 主路径线性序）
    /// 产出 <see cref="StaticReference"/>（原始 (src,tgt,refType) 三元组），供上层守卫幂等写入引用图；
    /// 端点 Id 复用 <see cref="NodeRelationFactory"/>。
    /// 注意：控制流边不落入 <see cref="NodeRelationType"/>（KeyMappingToNode/NodeToNode）两类数据流/归属边，
    /// 与 <see cref="DeriveEdgesForNode"/> 的权威边集互不干扰（走独立的静态引用写路径）。
    /// </summary>
    public static IReadOnlyList<StaticReference> DeriveWorkflowControlFlowEdges(
        string workflowId, IReadOnlyList<string> stepNodeKeys)
    {
        var edges = new List<StaticReference>();
        if (string.IsNullOrWhiteSpace(workflowId) || stepNodeKeys is not { Count: > 0 })
            return edges;

        foreach (var key in stepNodeKeys)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            // 组合：工作流包含步骤节点（workflow → node）
            edges.Add(new StaticReference(
                NodeResourceTypes.Workflow, workflowId,
                NodeResourceTypes.Node, NodeRelationFactory.NodeId(workflowId, key),
                WorkflowReferenceTypes.Owns));
        }

        for (var i = 0; i + 1 < stepNodeKeys.Count; i++)
        {
            var from = stepNodeKeys[i];
            var to = stepNodeKeys[i + 1];
            if (string.IsNullOrWhiteSpace(from) || string.IsNullOrWhiteSpace(to)) continue;
            // 控制流：上一步骤 → 下一步骤（node → node）
            edges.Add(new StaticReference(
                NodeResourceTypes.Node, NodeRelationFactory.NodeId(workflowId, from),
                NodeResourceTypes.Node, NodeRelationFactory.NodeId(workflowId, to),
                WorkflowReferenceTypes.Calls));
        }
        return edges;
    }

    /// <summary>导出边（对齐 public）：工作流 → 步骤节点，声明该节点跨工作流可见（workflow → node, Exports）。</summary>
    public static StaticReference DeriveExportsEdge(string workflowId, string nodeKey)
        => new(
            NodeResourceTypes.Workflow, workflowId,
            NodeResourceTypes.Node, NodeRelationFactory.NodeId(workflowId, nodeKey),
            WorkflowReferenceTypes.Exports);

    /// <summary>继承边（对齐类继承 is-a）：派生工作流 → 基类工作流（workflow → workflow, Inherits）。</summary>
    public static StaticReference DeriveInheritsEdge(string derivedWorkflowId, string baseWorkflowId)
        => new(
            NodeResourceTypes.Workflow, derivedWorkflowId,
            NodeResourceTypes.Workflow, baseWorkflowId,
            WorkflowReferenceTypes.Inherits);

    /// <summary>接口实现边（对齐 C# interface）：工作流 → 契约，声明实现某端口契约（workflow → contract, Implements）。</summary>
    public static StaticReference DeriveImplementsEdge(string workflowId, string contractId)
        => new(
            NodeResourceTypes.Workflow, workflowId,
            NodeResourceTypes.Workflow, contractId,
            WorkflowReferenceTypes.Implements);

    /// <summary>依赖边（对齐 using）：工作流 → 依赖资源，显式声明上下文依赖（workflow → resource, DependsOn）。</summary>
    public static StaticReference DeriveDependsOnEdge(string workflowId, string resourceId)
        => new(
            NodeResourceTypes.Workflow, workflowId,
            NodeResourceTypes.Workflow, resourceId,
            WorkflowReferenceTypes.DependsOn);

    private static NodeRelationType ResolveRelationType(string refType)
    {
        if (string.Equals(refType, ReferenceTypes.KeyMappingToNode, StringComparison.OrdinalIgnoreCase))
            return NodeRelationType.KeyMappingToNode;
        // 空白类型按默认 NodeToNode（兼容存量声明）；非空未知类型 fail-fast，不再静默降级 NodeToNode（对齐 UniversalReference 注册表校验）。
        if (string.IsNullOrWhiteSpace(refType) || string.Equals(refType, ReferenceTypes.NodeToNode, StringComparison.OrdinalIgnoreCase))
            return NodeRelationType.NodeToNode;
        throw new InvalidOperationException($"未知的引用类型 '{refType}'（声明边仅支持 {ReferenceTypes.KeyMappingToNode} / {ReferenceTypes.NodeToNode}）。");
    }

}
