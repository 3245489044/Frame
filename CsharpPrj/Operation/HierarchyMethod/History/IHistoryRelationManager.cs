using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Operation.HierarchyMethod.History;

/// <summary>
/// 历史关系管理端口（History = Task 更上层的 class 容器，归拢多 task）—— 纯抽象，零实现依赖。
/// 分层口径：本端口**只承载 history 层关系**——历史创建/删除、历史归拢任务（history→task）、
/// 派生节点读（= 归拢任务的节点并集）、会话归属边、图查询。
/// <para>
/// task→node 归属（cross_db_link）属**节点执行层**（attribution.ushar / INodeAttributionFlow），
/// 不在本端口（禁 history 直连 node）；`history_owns_node` 边已退役为派生视图。
/// </para>
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

    /// <summary>返回该历史归拢的任务键集（History class 容器成员）。</summary>
    Task<IReadOnlyList<string>> GetTaskKeysAsync(string historyId, CancellationToken ct = default);

    /// <summary>返回该历史归拢的任务链接记录。</summary>
    Task<IReadOnlyList<HistoryTaskLink>> GetTasksAsync(string historyId, CancellationToken ct = default);

    /// <summary>返回该历史挂载的业务节点键（历史作用域寻址）。**派生**：= 该历史全部任务的节点并集。</summary>
    Task<IReadOnlyList<string>> GetNodeKeysAsync(string historyId, string workflowId, CancellationToken ct = default);

    /// <summary>会话归属边（session_owns）：会话消息节点 → 会话根（入史层挂载；幂等加法）。</summary>
    Task AddSessionMessageLinkAsync(string sessionName, string workflowId, string nodeKey, CancellationToken ct = default);

    /// <summary>列出全部历史**容器**（枚举图中 ResourceType=history 端点）。
    /// 与 <c>IHistoryStore.ListHistoryIdsAsync</c>（ur_histories **表行**）语义不同——本方法读**图端点**（D2 消歧：
    /// 会话/历史「存在性」唯一真源 = ur_histories，图只作归属边真源，见 CONV-SS）。</summary>
    Task<IReadOnlyList<string>> ListHistoryContainersAsync(CancellationToken ct = default);
}
