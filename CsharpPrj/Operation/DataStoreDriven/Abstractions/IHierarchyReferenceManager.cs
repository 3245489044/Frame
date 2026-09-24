using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.DataStoreDriven.Abstractions
{
    /// <summary>
    /// 三层层级引用管理端口（History→Task→Node 统一层级，LiteDB 引用管理的**唯一中立入口**）。
    /// 数据模型：单 history（顶层 class 容器）归拢多 task，task 以 M:N 关联多 node（同一节点可被多 task 共享引用）。
    /// <para>
    /// 模块化分离边界（用户确认语义 = 真多对多）：本端口只描述「层级实体之间存在哪种引用边」，
    /// 实现 = <see cref="ReferenceGrounding"/>（唯一守卫）→ <see cref="Graph.ReferenceGraphCoordinator"/>（唯一 LiteDB 写边 owner）。
    /// 三层实例（<c>HierarchyMethod.Nodes/Tasks/History</c> 的 NodeRecord/TaskRecord/HistoryRecord）**只依赖本接口**、
    /// 不触碰任何 LiteDB 具体类型，引用管理与实例承载据此正式切开。
    /// </para>
    /// <para>
    /// 边类型见 <see cref="CrossDbReferenceTypes"/>：<c>history_owns_task</c>（history→task 1:N）、
    /// <c>cross_db_link</c>（task→node M:N）。历史节点集**派生** = 其全部任务的 task→node 边并集
    /// （<c>history_owns_node</c> 已降级为派生视图，不再独立存边）。
    /// </para>
    /// </summary>
    public interface IHierarchyReferenceManager
    {
        // ---- history 根（单 history = 顶层 class 容器）----
        /// <summary>幂等建 history.{historyId} 根端点。</summary>
        Task EnsureHistoryAsync(string historyId, CancellationToken ct = default);

        /// <summary>删除历史（级联清其 history_owns_task 出边）。返回是否命中删除。</summary>
        Task<bool> DeleteHistoryAsync(string historyId, CancellationToken ct = default);

        // ---- history → task（1:N，幂等）----
        Task AttachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct = default);
        Task DetachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct = default);

        /// <summary>该历史归拢的任务键集（History class 容器成员）。</summary>
        Task<IReadOnlyList<string>> GetHistoryTaskKeysAsync(string historyId, CancellationToken ct = default);

        // ---- task ↔ node（M:N，一条 (task,workflowId,nodeKey) 一条 cross_db_link 边）----
        /// <summary>节点关联到任务（幂等加法；同一节点可被多任务共享引用）。</summary>
        Task LinkTaskNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default);

        /// <summary>解除节点与任务的关联（M:N 撤销边）。</summary>
        Task UnlinkTaskNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default);

        /// <summary>该 task 关联的节点键集（task 作用域寻址）。</summary>
        Task<IReadOnlyList<string>> GetTaskNodeKeysAsync(string taskKey, string workflowId, CancellationToken ct = default);

        /// <summary>该历史的工作流节点键集（**派生**：= 其全部任务的节点并集，不依赖 history_owns_node）。</summary>
        Task<IReadOnlyList<string>> GetHistoryNodeKeysAsync(string historyId, string workflowId, CancellationToken ct = default);

        // ---- session（会话图子图：session.{name} 出边挂载的消息/产出节点；与 task 作用域正交）----
        /// <summary>会话子图节点键集（按 <c>session_owns</c> 出边返回会话挂载的业务节点键）。
        /// 入史层（L-B）读侧唯一入口：键形 <c>session.{name}.msg.{seq}</c>，seq 为时序权威。</summary>
        Task<IReadOnlyList<string>> GetSessionNodeKeysAsync(string sessionName, string workflowId, CancellationToken ct = default);
    }
}
