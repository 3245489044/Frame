using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.DataStoreDriven.Abstractions
{
    /// <summary>
    /// 节点关系类别（图数据库存"节点之间的关系"的两类语义）：
    /// <list type="bullet">
    ///   <item><see cref="KeyMappingToNode"/>：KeyMapping ↔ 节点（某映射属于某节点 / 映射到某节点端口）</item>
    ///   <item><see cref="NodeToNode"/>：节点 ↔ 节点（上游节点输出端口 → 下游节点输入端口的数据流组合边）</item>
    /// </list>
    /// </summary>
    public enum NodeRelationType
    {
        KeyMappingToNode,
        NodeToNode,
    }

    /// <summary>
    /// 一条节点关系（有向边）。端点为通用资源三元组（资源类型 + Id），
    /// 由 <see cref="NodeRelationFactory"/> 按类别构造（节点/KeyMapping/快照任务池资源）。
    /// </summary>
    public sealed class NodeRelation
    {
        public NodeRelationType Type { get; init; }

        public string SourceResourceType { get; init; } = NodeResourceTypes.Node;

        public string SourceId { get; init; } = string.Empty;

        public string TargetResourceType { get; init; } = NodeResourceTypes.Node;

        public string TargetId { get; init; } = string.Empty;

        /// <summary>上游/源端输出端口（NodeToNode 数据流边）。</summary>
        public string? SourcePort { get; init; }

        /// <summary>下游/目标端输入端口（NodeToNode 数据流边）。</summary>
        public string? TargetPort { get; init; }

        /// <summary>关联的 KeyMapping 存储键（三库收敛后指向节点库承载的分类键，KeyMapping↔节点 关系的载荷）。</summary>
        public string? KeyMappingRef { get; init; }

        public double Strength { get; init; } = 1.0;
    }

    /// <summary>节点关系端点资源类型常量（资源在引用图中的 ResourceType）。值对齐 <see cref="NodeTypes"/>，避免常量重复定义。</summary>
    public static class NodeResourceTypes
    {
        public const string Node = NodeTypes.Node;
        public const string KeyMapping = NodeTypes.KeyMapping;

        /// <summary>任务资源类型（task 镜像，补全关联缺口；taskKey 作为图资源 Id，对齐 <see cref="NodeTypes"/> 判别）。</summary>
        public const string Task = "task";

        /// <summary>会话根资源类型（方案 C：会话图子图锚点；Id = "session.{name}"，NodeType=session 的图端点镜像）。</summary>
        public const string Session = "session";

        /// <summary>历史 class 容器资源类型（History=Task 更上层的容器，归拢多 task 多 node；Id = "history.{historyId}"）。</summary>
        public const string History = "history";

        /// <summary>工作流资源类型（工作流控制流/组合边的源端点；Id = workflowId，对齐 DSL [Node] workflow）。</summary>
        public const string Workflow = "workflow";
    }

    /// <summary>工作流静态引用类型常量（对齐 DSL <c>[Edge] Calls</c> / <c>[Edge] Owns</c>）。</summary>
    public static class WorkflowReferenceTypes
    {
        /// <summary>控制流：上一步骤调用下一步骤（node → node，按 RoundSpec 主路径线性序）。</summary>
        public const string Calls = "Calls";

        /// <summary>组合：工作流包含步骤（workflow → node）。</summary>
        public const string Owns = "Owns";

        /// <summary>跨工作流可见性边界（对齐 public 成员）：导出节点对其它工作流可见（workflow → node）。</summary>
        public const string Exports = "Exports";

        /// <summary>工作流模板继承（对齐类继承 is-a）：派生工作流 → 基类工作流（workflow → workflow）。</summary>
        public const string Inherits = "Inherits";

        /// <summary>接口实现（对齐 C# interface）：工作流声明实现某端口契约（workflow → contract）。</summary>
        public const string Implements = "Implements";

        /// <summary>编译期依赖（对齐 using）：显式声明上下文依赖（workflow → resource）。</summary>
        public const string DependsOn = "DependsOn";
    }

    /// <summary>
    /// 节点关系构造工厂 —— 按工作流节点语义构造三类关系的端点资源（资源 Id 以 workflowId 命名空间隔离）。
    /// </summary>
    public static class NodeRelationFactory
    {
        /// <summary>节点资源 Id：{workflowId}.{nodeKey}（同 KeyMapping 节点级规范键，见 <see cref="KeyMappingKeys.NodeLevelKey"/>）。</summary>
        public static string NodeId(string workflowId, string nodeKey) => KeyMappingKeys.NodeLevelKey(workflowId, nodeKey);

        /// <summary>KeyMapping 资源 Id：{workflowId}.{key}（节点级规范键，见 <see cref="KeyMappingKeys.NodeLevelKey"/>）。</summary>
        public static string KeyMappingId(string workflowId, string key) => KeyMappingKeys.NodeLevelKey(workflowId, key);

        /// <summary>KeyMapping ↔ 节点：keymapping 资源 → 节点资源。</summary>
        public static NodeRelation KeyMappingToNode(string workflowId, string keyMappingKey, string nodeKey)
            => new()
            {
                Type = NodeRelationType.KeyMappingToNode,
                SourceResourceType = NodeResourceTypes.KeyMapping,
                SourceId = KeyMappingId(workflowId, keyMappingKey),
                TargetResourceType = NodeResourceTypes.Node,
                TargetId = NodeId(workflowId, nodeKey),
                KeyMappingRef = KeyMappingKeys.NodeLevelKey(workflowId, keyMappingKey),
            };

        /// <summary>节点 ↔ 节点：上游节点输出端口 → 下游节点输入端口（KeyMapping 驱动的数据流组合边）。</summary>
        public static NodeRelation NodeToNode(string workflowId, string fromNode, string fromPort, string toNode, string toPort, string? keyMappingRef = null)
            => new()
            {
                Type = NodeRelationType.NodeToNode,
                SourceResourceType = NodeResourceTypes.Node,
                SourceId = NodeId(workflowId, fromNode),
                SourcePort = fromPort,
                TargetResourceType = NodeResourceTypes.Node,
                TargetId = NodeId(workflowId, toNode),
                TargetPort = toPort,
                KeyMappingRef = keyMappingRef,
            };
    }

    /// <summary>
    /// 节点关系存储端口（图数据库存"节点之间的关系"）：
    /// 把两类关系（KeyMapping↔节点 / 节点↔节点）以图资源 + 引用边的形式持久化，
    /// 供工作流节点自由组合（KeyMapping 驱动）。
    /// </summary>
    public interface INodeRelationStore
    {
        Task InitializeAsync(CancellationToken ct = default);

        /// <summary>确保节点资源存在于图（幂等；节点即存储单元在关系图中的镜像）。</summary>
        Task EnsureNodeAsync(string workflowId, string nodeKey, string? symbol = null,
            IReadOnlyDictionary<string, object>? metadata = null, CancellationToken ct = default);

        /// <summary>写入一条节点关系（幂等：同类型同端点先删后加）。</summary>
        Task AddRelationAsync(string workflowId, NodeRelation relation, CancellationToken ct = default);

        /// <summary>删除一条节点关系。</summary>
        Task RemoveRelationAsync(string workflowId, NodeRelation relation, CancellationToken ct = default);

        /// <summary>按节点检索关系（可限定类别；includeIncoming 控制是否含入边）。</summary>
        Task<IReadOnlyList<NodeRelation>> GetRelationsAsync(string workflowId, string nodeKey,
            NodeRelationType? type = null, bool includeIncoming = true, CancellationToken ct = default);

        /// <summary>按关系类别取相邻节点（outgoing=true 取下游，false 取上游）。</summary>
        Task<IReadOnlyList<string>> GetRelatedNodesAsync(string workflowId, string nodeKey,
            NodeRelationType type, bool outgoing = true, CancellationToken ct = default);
    }
}
