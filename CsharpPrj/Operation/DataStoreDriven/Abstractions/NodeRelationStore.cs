using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.Node.Relations;

namespace Operation.DataStoreDriven.Abstractions
{
    /// <summary>
    /// 节点关系存储端口（图数据库存"节点之间的关系"）：
    /// 把两类关系（KeyMapping↔节点 / 节点↔节点）以图资源 + 引用边的形式持久化，
    /// 供工作流节点自由组合（KeyMapping 驱动）。
    /// <para>
    /// 关系语义类型（<see cref="NodeRelation"/> / <see cref="NodeRelationType"/>）已上移 L2 通用节点框架
    /// <c>Modules.Node.Relations</c>，本文件只留端口接口（B1 二批）。
    /// </para>
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

        /// <summary>按关系类别取相邻节点**键**（outgoing=true 取下游，false 取上游）。返回值为节点键（非完整资源 Id）：非本工作流前缀的端点不透出。</summary>
        Task<IReadOnlyList<string>> GetRelatedNodesAsync(string workflowId, string nodeKey,
            NodeRelationType type, bool outgoing = true, CancellationToken ct = default);
    }
}
