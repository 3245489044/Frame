using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UniversalWorkflowFramework.Core.Models;

namespace Operation.DataStoreDriven.Abstractions
{
    /// <summary>
    /// 对账作用域：单节点 / 全图（守卫编排的 Reconcile 两级入口）。
    /// </summary>
    public enum MaintenanceScope
    {
        /// <summary>单节点：仅对账指定节点（携带 workflowId + nodeKey）。</summary>
        Node,

        /// <summary>全图：对账全部业务节点（复用全量对账器语义）。</summary>
        All,
    }

    /// <summary>
    /// 引用关系维护守卫（唯一守卫入口，对齐 SingleImplementation / TH-12）。
    /// 业务/执行/UI/声明/存储桥一律经本端口写入引用层；本守卫只负责「何时写 / 推导出哪些边 / 对账编排」，
    /// 写边原语仍在 <c>ReferenceGraphCoordinator</c>（纯执行者，本守卫不复制写边逻辑）。
    /// 装配可空：未装配守卫（DB-only / 无引用层）的调用方跳过，不依赖具体装饰器类型（对齐「移除运行时类型判断」）。
    /// </summary>
    public interface IReferenceMaintenance
    {
        /// <summary>镜像业务节点内容（upsert：存在更新 symbol/metadata，不存在新增；不删引用边）。</summary>
        Task MirrorNodeAsync(string workflowId, NodeRecord node, CancellationToken ct = default);

        /// <summary>镜像 node 端点（业务节点写透，白名单仅 {node}）。</summary>
        Task EnsureNodeMirrorAsync(string workflowId, string nodeKey, string? symbol = null, CancellationToken ct = default);

        /// <summary>移除 node 端点镜像（级联清边）。</summary>
        Task RemoveNodeMirrorAsync(string workflowId, string nodeKey, CancellationToken ct = default);

        /// <summary>镜像 task 端点（Id=taskKey，幂等）。</summary>
        Task EnsureTaskMirrorAsync(string taskKey, CancellationToken ct = default);

        /// <summary>移除 task 端点镜像（级联清边）。</summary>
        Task RemoveTaskMirrorAsync(string taskKey, CancellationToken ct = default);

        /// <summary>权威边集写入：以 <paramref name="authoritative"/> 为权威边集，收敛到图（经协调器原子 apply，删残留 + 补缺失）。</summary>
        Task WriteRelationsAsync(string workflowId, string nodeKey,
            IReadOnlyCollection<NodeRelation> authoritative, CancellationToken ct = default);

        /// <summary>task 关联节点：注册 task 端点 + 与关联节点建 task→node 边（幂等；增量维护任务关联）。</summary>
        Task ReconcileTaskAsync(string taskKey, IReadOnlyCollection<string> relatedNodeKeys,
            string workflowId = "", CancellationToken ct = default);

        /// <summary>运行时 task 归属边（cross_db_link，真多对多）：task（ur_tasks）→ 节点（ur_nodes）。幂等加法，
        /// 同一节点可被多 task 共享引用；不并入数据流权威边集（避免 WriteRelationsAsync 按目标节点删残留时误删本归属边）。</summary>
        Task EnsureTaskNodeLinkAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default);

        /// <summary>解除 task 与节点的关联边（M:N 撤销：task→node cross_db_link 出边移除；节点删除时须级联清多 task 归属）。</summary>
        Task UnlinkTaskNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default);

        /// <summary>历史 class 容器根端点：幂等建 history.{historyId} 端点镜像（History=Task 更上层容器，归拢多 task 多 node）。</summary>
        Task EnsureHistoryMirrorAsync(string historyId, CancellationToken ct = default);

        /// <summary>移除历史根端点镜像（级联清边；删除历史即删除其引用图锚点）。</summary>
        Task RemoveHistoryMirrorAsync(string historyId, CancellationToken ct = default);

        /// <summary>历史归拢任务：history.{historyId} → task（taskKey）引用边（幂等加法；一个历史归拢多个任务）。</summary>
        Task AttachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct = default);

        /// <summary>解除历史归拢任务边（历史 → task 出边移除）。</summary>
        Task DetachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct = default);

        /// <summary>按历史查 history_owns_task 出边，返回该历史归拢的任务键集（History class 容器成员）。</summary>
        Task<IReadOnlyList<string>> GetHistoryTasksAsync(string historyId, CancellationToken ct = default);

        /// <summary>历史挂载节点：history.{historyId} → wf.node（history_owns_node 边，幂等加法；历史 class 容器归拢节点产出）。</summary>
        Task AttachHistoryNodeAsync(string historyId, string workflowId, string nodeKey, CancellationToken ct = default);

        /// <summary>按历史查 history_owns_node 出边，返回该历史挂载的工作流节点键集（历史作用域寻址）。</summary>
        Task<IReadOnlyList<string>> GetHistoryNodeKeysAsync(string historyId, string workflowId, CancellationToken ct = default);

        /// <summary>会话归属边（session_owns）：把业务节点（消息/工作流产出）挂到会话根（幂等加法）。</summary>
        Task AddSessionMessageLinkAsync(string sessionName, string workflowId, string nodeKey, CancellationToken ct = default);

        /// <summary>会话时序边（round_sequence）：消息节点 → 下一消息节点（历史顺序由引用边决定）。</summary>
        Task AddRoundSequenceEdgeAsync(string workflowId, string fromNode, string toNode, CancellationToken ct = default);

        /// <summary>会话子图节点键集：按 session_owns 出边返回会话挂载的业务节点键（会话作用域寻址）。</summary>
        Task<IReadOnlyList<string>> GetSessionNodeKeysAsync(string sessionName, string workflowId, CancellationToken ct = default);

        /// <summary>工作流静态引用（组合 + 控制流）幂等写入：建端点 → 删同端点同 refType 旧边 → 新增。
        /// 供「把声明式工作流控制流静态化为可维护引用关系」（Owns：工作流包含步骤；Calls：上一步骤调用下一步骤）消费；
        /// 与 <see cref="WriteRelationsAsync"/>（节点关系权威边集，NodeToNode/KeyMappingToNode）互补互不干扰。</summary>
        Task WriteStaticReferencesAsync(string workflowId,
            IReadOnlyCollection<StaticReference> references, CancellationToken ct = default);

        /// <summary>主动声明关系（G3）：批量写 ur_nodes（NodeType=relation）+ 推导边写入图（声明优先，幂等）。</summary>
        Task DeclareRelationsAsync(string workflowId, IReadOnlyCollection<RelationDeclarationEntry> declarations, CancellationToken ct = default);

        /// <summary>撤销声明：更新 Status=revoked（或删除节点）→ 级联清边。</summary>
        Task UndeclareRelationsAsync(string workflowId, IReadOnlyCollection<string> relationIds, CancellationToken ct = default);

        /// <summary>列出全部声明（含状态）；workflowId 为空则列全部。</summary>
        Task<IReadOnlyList<RelationDeclarationEntry>> ListRelationsAsync(string? workflowId = null, CancellationToken ct = default);

        /// <summary>对账（单节点 / 全图两级），返回处理节点数（携带关系状态并已收敛的业务节点数）。</summary>
        Task<int> ReconcileAsync(MaintenanceScope scope, string? workflowId = null, string? nodeKey = null, CancellationToken ct = default);

        /// <summary>全量重建：清空图库 → 从 SQL 权威重建（业务节点镜像 + task 镜像 + 声明关系边 + 执行推导边），幂等可重复。</summary>
        Task RebuildAllAsync(CancellationToken ct = default);
    }
}
