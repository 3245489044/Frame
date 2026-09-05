using System.Collections.Generic;
using Share.HierarchySupport;
using Share.TaskStatusPool.Loops;
using Share.TaskStatusPool.TaskPool;

namespace Operation.HierarchyMethod.Tasks.Loops
{
    /// <summary>
    /// 统一任务快照状态契约（任务 = 快照的入口契约，快照 = 任务的运行时演化；修改任务 = 修改快照）。
    /// 在 <see cref="ILoopState"/>（算法层依赖的统一上下文：Budget/Token/Flag/Round/LastEval）之上，
    /// 叠加 Agent 扩展字段（生命周期 Status / 作用域 Scope / 连续失败 / 策略视图 / 运行中执行状态 / 转移历史 / 类型化 DataSpace）。
    /// 消费方：<see cref="Agent.State.Runtime.AiSchedulerStateMachine"/>（混合调度机）只依赖本契约；
    /// 状态宿主（<see cref="Agent.Core.TaskPool.AgentTaskItem"/>）由消费方实现。
    /// 本接口承载于 Agent.Contract（Agent.State 与 Agent.Core 均可引用），避免 Agent.State → Agent.Core 反向依赖。
    /// </summary>
    public interface IAgentLoopState : ILoopState
    {
        /// <summary>任务生命周期状态（统一状态机；暂停/恢复直接改此字段）。权威来源（嵌于 PayloadJson）。</summary>
        TaskExecutionStatus Status { get; set; }

        /// <summary>错误处理作用域（Scope），决定 handle 的合法动作子集。</summary>
        Scope Scope { get; set; }

        /// <summary>连续失败计数（决策遥测输入；规则见 ContinuousFailureRule）。</summary>
        int ContinuousFailures { get; set; }

        /// <summary>当前生效策略 Id（策略视图 Current）。</summary>
        string CurrentStrategyId { get; set; }

        /// <summary>可用策略集（防幻觉目标校验权威来源）。</summary>
        List<string> AvailableStrategyIds { get; }

        /// <summary>运行中执行状态：executorId → 离散状态 Id（如 AgentRunStatus 的名称）。</summary>
        Dictionary<string, string> AgentStatus { get; }

        /// <summary>近期转移历史（只增；压缩由宿主 CompactProcedure 负责）。</summary>
        List<string> RecentTransitions { get; }

        /// <summary>类型化数据空间（统一数据容器，单一数据持有方）。遮蔽 <see cref="ILoopState.Data"/> 的字典视图。</summary>
        new DataSpace Data { get; }

        /// <summary>把本权威根投影为只读运行时快照（<see cref="AgentRuntimeSnapshot"/>，供 RuntimeStateBroker 发布 / 外部订阅 / UI 查询）。</summary>
        AgentRuntimeSnapshot ToRuntimeSnapshot(string stage);
    }
}
