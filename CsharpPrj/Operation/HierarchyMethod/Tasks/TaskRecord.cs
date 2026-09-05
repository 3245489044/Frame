using System;
using System.Collections.Generic;
using Share.TaskStatusPool.TaskPool;

namespace Operation.HierarchyMethod.Tasks
{
    /// <summary>
    /// 纯任务簿记实体（任务池 ur_tasks 载荷，task 层唯一操作对象）——只承载"任务本身"的簿记字段，
    /// 不感知执行器 / LLM / 循环快照（clinerules：task=纯簿记，L1 控制流）。
    /// 实现 <see cref="ITaskPoolItem"/>（通用任务池条目契约）+ <see cref="IStatusAwareTaskPoolItem"/>（Status 快照权威）
    /// + <see cref="IActivatableTaskPoolItem"/>（Active 激活标志权威）。
    /// LLM/执行相关逻辑封装为**执行节点**（<see cref="ExecutionNode"/>），由 task 作为**属性直接携带**
    /// （相当于任务的一个属性；task 不调用 LLM，节点由执行器消费时解析执行）。
    /// 循环快照根（Budget/Token/Flag/Round 等确定性核心）**不在本实体**，见 <see cref="AgentLoopState"/>。
    /// </summary>
    public sealed class TaskRecord : ITaskPoolItem, IStatusAwareTaskPoolItem, IActivatableTaskPoolItem
    {
        /// <summary>业务幂等键（去重；同时作为持久化键）。</summary>
        public string TaskKey { get; set; } = Guid.NewGuid().ToString("N");

        /// <summary>任务生命周期状态（统一状态机；暂停/恢复直接改此字段）。</summary>
        public TaskExecutionStatus Status { get; set; } = TaskExecutionStatus.NotStarted;

        /// <summary>激活标志（与写入解耦）：true=写入后进入消费循环可被采用 / false=仅持久化休眠，不被消费。
        /// 消费门（<see cref="TaskPool{TItem,TResult}"/>）仅采用「NotStarted 且 Active=true」的任务；休眠任务经激活（op=activate）翻转后唤醒。</summary>
        public bool Active { get; set; } = true;

        /// <summary>优先级（越大越先出队）。</summary>
        public int Priority { get; set; }

        /// <summary>失败最大重试次数。</summary>
        public int MaxRetries { get; set; }

        /// <summary>归属的历史 class 容器 Id（History=Task 更上层容器；可空 = 未归拢到历史）。
        /// 注：历史↔任务归属**权威承载**在引用图（history_owns_task 边，见 <c>IHierarchyReferenceManager</c>）；
        /// 本字段仅为实例内冗余视图，不作为图关系真源。</summary>
        public string? HistoryId { get; set; }

        /// <summary>会话/线程关联键（run 级事件关联；可为空）。</summary>
        public string? ThreadId { get; set; }

        /// <summary>输入键（结果写回 ur_outputs 的关联；可为空）。</summary>
        public string? InputKey { get; set; }

        /// <summary>任务指令/输入（作为 Agent 的 user_input）。</summary>
        public string UserInput { get; set; } = string.Empty;

        /// <summary>关联节点引用（nodeKey 列表，task op=enqueue 专用声明；纯引用，不读 payload）。
        /// 注：task↔node 关联**权威承载**在引用图（cross_db_link 边，真多对多，见 <c>IHierarchyReferenceManager</c>）；
        /// 本字段仅为实例内冗余视图，不作为图关系真源。</summary>
        public IReadOnlyList<string> RelatedNodes { get; set; } = Array.Empty<string>();

        /// <summary>执行节点 —— 把 LLM/执行逻辑封装为节点，由 task 作为属性携带（task 不调用 LLM，节点由执行器消费时解析执行）。
        /// null = 纯簿记任务，零 LLM 默认执行。</summary>
        public ExecutionNode? ExecutionNode { get; set; }

        /// <summary>创建时间。</summary>
        public DateTimeOffset CreatedAt { get; set; } = DateTimeOffset.UtcNow;

        /// <summary>最近更新时间。</summary>
        public DateTimeOffset UpdatedAt { get; set; } = DateTimeOffset.UtcNow;
    }
}
