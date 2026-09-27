using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.HierarchyMethod.History;

/// <summary>
/// 历史关系管理端口（History = Task 更上层的 class 容器，归拢多 task 多 node）—— 纯抽象，零实现依赖。
/// 管理 task / nodes / history 之间的逻辑关系：历史创建/删除、历史归拢任务、历史挂载节点、图查询。
/// 实现（Agent.Reference）只编排，写边原语委托 <c>IReferenceMaintenance</c>/图协调器（SingleImplementation/TH-12）。
/// 装配可空：未装配时消费方跳过，不阻断运行（对齐「未装配返回诊断」模式）。
/// </summary>
public interface IHistoryRelationManager
{
    /// <summary>幂等建历史根端点（history.{historyId}）。</summary>
    Task<HistoryRecord> CreateAsync(string historyId, string? name = null, CancellationToken ct = default);

    /// <summary>删除历史（级联清其引用边）。返回是否命中删除。</summary>
    Task<bool> DeleteAsync(string historyId, CancellationToken ct = default);

    /// <summary>历史归拢任务（history → task，history_owns_task 边；幂等）。</summary>
    Task AttachTaskAsync(string historyId, string taskKey, CancellationToken ct = default);

    /// <summary>解除历史归拢任务边。</summary>
    Task DetachTaskAsync(string historyId, string taskKey, CancellationToken ct = default);

    /// <summary>task → node 关联（cross_db_link，真多对多；幂等加法）。同一节点可被多 task 共享引用，
    /// 历史节点集经此边派生，故本方法是历史挂载节点的**新权威写路径**。</summary>
    Task LinkNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default);

    /// <summary>解除 task → node 关联（M:N 撤销边；仅解除本 task 归属，不影响其它 task）。</summary>
    Task UnlinkNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default);

    /// <summary>返回该历史归拢的任务键集（History class 容器成员）。</summary>
    Task<IReadOnlyList<string>> GetTaskKeysAsync(string historyId, CancellationToken ct = default);

    /// <summary>返回该历史归拢的任务链接记录。</summary>
    Task<IReadOnlyList<HistoryTaskLink>> GetTasksAsync(string historyId, CancellationToken ct = default);

    /// <summary>返回该历史挂载的业务节点键（历史作用域寻址）。**派生**：= 该历史全部任务的节点并集。</summary>
    Task<IReadOnlyList<string>> GetNodeKeysAsync(string historyId, string workflowId, CancellationToken ct = default);

    /// <summary>会话归属边（session_owns）：会话消息节点 → 会话根（入史层挂载；幂等加法）。</summary>
    Task AddSessionMessageLinkAsync(string sessionName, string workflowId, string nodeKey, CancellationToken ct = default);

    /// <summary>列出全部历史（供 UI 会话下拉 / 历史枚举）。</summary>
    Task<IReadOnlyList<string>> ListHistoryIdsAsync(CancellationToken ct = default);
}
