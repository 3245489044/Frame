using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Modules.DeclarativeDataflow.Model;
using Modules.DeclarativeDataflow.Processing;
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

    // ---- 查询（成员集经引用边推导；**无界**：不施加展开预算）----
    // 遍历实现与下方「层次处理与查询」组同一（经声明式引擎；schema 无法表达该查询时回退 owner 侧直读端口）。
    // 二者分工：本组 = schema 无关、无预算、只返回键集；引擎组 = schema 驱动、带显式预算、返回层引用/结论。
    /// <summary>该历史归拢的任务键集（无界成员集；遍历复用声明式引擎，schema 无法表达时回退 owner 侧直读）。</summary>
    Task<IReadOnlyList<string>> GetTasksAsync(string historyId, CancellationToken ct = default);

    /// <summary>该 task 关联的节点键集（无界成员集；遍历复用声明式引擎，schema 无法表达时回退 owner 侧直读）。</summary>
    Task<IReadOnlyList<string>> GetNodesAsync(string taskKey, string workflowId, CancellationToken ct = default);

    /// <summary>该历史的节点键集（派生：= 其全部任务节点并集；无界成员集，遍历复用声明式引擎）。</summary>
    Task<IReadOnlyList<string>> GetHistoryNodesAsync(string historyId, string workflowId, CancellationToken ct = default);

    // ---- 三层实例持久化（NodeOutputStore）----
    /// <summary>持久化单端口节点输出（ur_nodes NodeType=output，重启可恢复）。</summary>
    Task PersistNodeOutputAsync(string workflowId, string nodeKey, string port, object? value, CancellationToken ct = default);

    // ---- 层次处理与查询（声明式引擎：遍历 / 终止 / 子结论收敛由 Modules.DeclarativeDataflow 执法）----
    // 层数/层名/包含关系外置为 HierarchySchema 数据；本组方法是「消费方经统一门面即可用框架」的入口，
    // 与上面的成员集查询（owner 侧派生、无上限）分工：本组带**显式预算**（截断可在结论上观测）。
    /// <summary>按层处理以历史为根的层次（history → task → node），返回根结论（子层结论已按方向聚合）。</summary>
    Task<LevelProcessResult> ProcessHistoryAsync(string historyId, HierarchyProcessPlan? plan = null, CancellationToken ct = default);

    /// <summary>按层处理以任务为根的层次（task → node；自递归 schema 下即不定深度任务树）。</summary>
    Task<LevelProcessResult> ProcessTaskAsync(string taskKey, HierarchyProcessPlan? plan = null, CancellationToken ct = default);

    /// <summary>
    /// 按**任务自递归**层次处理（task → sub-task → … → node，返回根结论，子层结论已按方向聚合）。
    /// <para>
    /// 与 <see cref="ProcessTaskAsync"/> 的差别仅在 schema：本方法用 `task_owns_task` 自环表达**不定深度**子任务树，
    /// 故必须配展开预算（<see cref="HierarchyProcessPlan.Budget"/>，缺省为有限预算）；子任务边未建模时退化为单层。
    /// </para>
    /// </summary>
    Task<LevelProcessResult> ProcessTaskTreeAsync(string taskKey, HierarchyProcessPlan? plan = null, CancellationToken ct = default);

    /// <summary>取后代集合（广度优先、按实体去重、带环守卫与显式深度上限；如 history → 全部 task / node）。</summary>
    Task<IReadOnlyList<LevelRef>> DescendantsAsync(LevelRef node, string? stopLevelId = null, int maxDepth = 64, CancellationToken ct = default);

    /// <summary>取祖先链（自近及远，如 node → task → history）；未装配引用图读端口时返回空链。</summary>
    Task<IReadOnlyList<LevelRef>> AncestorsAsync(LevelRef node, CancellationToken ct = default);
}
