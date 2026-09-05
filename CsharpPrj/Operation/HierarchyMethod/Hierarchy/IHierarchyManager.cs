using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.History;
using Operation.HierarchyMethod.Tasks;

namespace Operation.HierarchyMethod.Hierarchy;

/// <summary>
/// 三层层级统一管理门面（多 nodes ↔ 多 tasks ↔ 单 history）—— 消费方（Agent / UI / StorageBridge）唯一入口。
/// <para>
/// 模块化分离边界（对齐用户确认的真多对多语义）：
///   · **引用管理**（LiteDB 边）经注入端口 <c>IHierarchyReferenceManager</c>（实现 = DataStoreDriven.ReferenceGrounding），
///     本门面及三层实例只依赖该接口、不触碰任何 LiteDB 具体类型；
///   · **三层实例**（NodeOutputStore / TaskRecord / 历史内容）由本门面编排持久化，实例本身零 LiteDB 依赖。
/// </para>
/// <para>
/// 数据模型：单 history（顶层 class 容器）→ 多 task；task ↔ node 为真多对多（同一节点可被多 task 共享引用）。
/// 历史节点集**派生** = 其全部任务的节点并集。装配可空：未装配引用端口 → 查询返回空集、写 no-op（对齐「未装配返回诊断」模式）。
/// </para>
/// </summary>
public interface IHierarchyManager
{
    // ---- history 根（单 history = 顶层 class 容器）----
    /// <summary>幂等建历史根端点 + 返回视图。未装配引用端口 → 返回仅含 Id 的空视图（不写图）。</summary>
    Task<HistoryRecord> CreateHistoryAsync(string historyId, string? name = null, CancellationToken ct = default);

    /// <summary>删除历史（级联清其引用边）。返回是否命中。</summary>
    Task<bool> DeleteHistoryAsync(string historyId, CancellationToken ct = default);

    // ---- task（注册 + 归拢历史）----
    /// <summary>注册任务（保存 TaskRecord；可选归拢到 history：建历史根 + 挂 history→task 边）。</summary>
    Task RegisterTaskAsync(string taskKey, string? historyId = null, TaskRecord? spec = null, CancellationToken ct = default);

    /// <summary>删除任务（保存侧删除 + 解除历史归拢边）。</summary>
    Task DeleteTaskAsync(string taskKey, CancellationToken ct = default);

    // ---- task ↔ node（真多对多）----
    /// <summary>节点关联到任务（幂等加法；同一节点可被多 task 共享引用）。</summary>
    Task LinkNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default);

    /// <summary>解除节点与任务的关联（M:N 撤销；不影响其它 task）。</summary>
    Task UnlinkNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default);

    // ---- 查询（成员集经引用边推导）----
    /// <summary>该历史归拢的任务键集。</summary>
    Task<IReadOnlyList<string>> GetTasksAsync(string historyId, CancellationToken ct = default);

    /// <summary>该 task 关联的节点键集。</summary>
    Task<IReadOnlyList<string>> GetNodesAsync(string taskKey, string workflowId, CancellationToken ct = default);

    /// <summary>该历史的节点键集（派生：= 其全部任务节点并集）。</summary>
    Task<IReadOnlyList<string>> GetHistoryNodesAsync(string historyId, string workflowId, CancellationToken ct = default);

    // ---- 三层实例持久化（NodeOutputStore）----
    /// <summary>持久化单端口节点输出（ur_nodes NodeType=output，重启可恢复）。</summary>
    Task PersistNodeOutputAsync(string workflowId, string nodeKey, string port, object? value, CancellationToken ct = default);
}
