using System;
using System.Collections.Generic;
using System.Linq;
using Share.HierarchySupport;
using Operation.HierarchyMethod.Tasks;
using Share.TaskStatusPool.Loops;
using Share.TaskStatusPool.TaskPool;

namespace Operation.HierarchyMethod.Tasks.Loops
{
    /// <summary>
    /// 循环快照根（运行时确定性核心，<see cref="ILoopState"/>）——与任务簿记（<see cref="TaskRecord"/>）解耦后的独立运行状态实体。
    /// 承载 Budget/Token/Flag/Round/LastEval/DataSpace（clinerules 确定性核心，SEP.DeterministicCoreDisjoint），
    /// 以及 Agent 扩展字段（Status/Scope/ContinuousFailures/策略视图/运行中执行状态/转移历史）。
    /// 由快照驱动循环引擎 / 单根循环驱动（Share.TaskStatusPool.Loops）作为循环 TState；
    /// 消费时从 <see cref="TaskRecord"/> 投影生成（TaskKey/UserInput/ThreadId/InputKey），执行策略不进入本实体。
    /// </summary>
    public sealed class AgentLoopState : IAgentLoopState
    {
        /// <summary>关联任务键（回指 <see cref="TaskRecord.TaskKey"/>；供持久化/取消/暂停定位）。</summary>
        public string TaskKey { get; set; } = Guid.NewGuid().ToString("N");

        // ---- ILoopState 确定性核心（唯一写入入口：BudgetEscalator/ProgressUpdater/Decide）----
        /// <summary>熔断等级 level∈{0..3}。</summary>
        public FailureBudget Budget { get; } = new();

        /// <summary>轮次进度 value∈{0..Max}。</summary>
        public ProgressToken Token { get; } = new();

        /// <summary>控制位掩码。</summary>
        public ControlFlag Flag { get; } = new();

        /// <summary>当前轮次号（从 1 起）。</summary>
        public int Round { get; set; }

        /// <summary>用户输入（从 TaskRecord 投影）。</summary>
        public string? UserInput { get; set; }

        /// <summary>上一轮执行结果文本。</summary>
        public string? LastResultText { get; set; }

        /// <summary>上一轮评估结果。</summary>
        public EvalOutcome LastEval { get; set; } = EvalOutcome.None;

        /// <summary>会话/线程关联键（结果写回 ur_outputs 的关联）。</summary>
        public string? ThreadId { get; set; }

        /// <summary>输入键（结果写回 ur_outputs 的关联）。</summary>
        public string? InputKey { get; set; }

        // ---- IAgentLoopState 扩展 ----
        /// <summary>任务生命周期状态。</summary>
        public TaskExecutionStatus Status { get; set; } = TaskExecutionStatus.NotStarted;

        /// <summary>错误处理作用域（Scope）。</summary>
        public Scope Scope { get; set; } = Scope.LoopBody;

        /// <summary>连续失败计数（决策遥测输入）。</summary>
        public int ContinuousFailures { get; set; }

        /// <summary>当前生效策略 Id（策略视图 Current）。</summary>
        public string CurrentStrategyId { get; set; } = "";

        /// <summary>可用策略集（防幻觉目标校验权威来源）。</summary>
        public List<string> AvailableStrategyIds { get; } = new();

        /// <summary>运行中执行状态：executorId → 离散状态 Id。</summary>
        public Dictionary<string, string> AgentStatus { get; } = new(StringComparer.Ordinal);

        /// <summary>近期转移历史（只增；压缩由宿主 CompactProcedure 负责）。</summary>
        public List<string> RecentTransitions { get; } = new();

        /// <summary>类型化数据空间（统一数据容器，单一数据持有方）。</summary>
        public DataSpace Data { get; } = new(validate: false);

        IDictionary<string, object> ILoopState.Data => Data;

        /// <summary>投影为只读运行时快照（供 RuntimeStateBroker 发布 / 外部订阅 / UI 查询）。</summary>
        public AgentRuntimeSnapshot ToRuntimeSnapshot(string stage)
            => new(
                Round: Round,
                Stage: RuntimeStage.Normalize(stage),
                BudgetLevel: Budget.Level,
                TokenValue: Token.Value,
                TokenMax: Token.Max,
                Halt: Flag.Halt,
                ContinuousFailures: ContinuousFailures,
                CurrentStrategyId: string.IsNullOrWhiteSpace(CurrentStrategyId) ? null : CurrentStrategyId,
                AvailableStrategyIds: new List<string>(AvailableStrategyIds),
                AgentStatuses: AgentStatus
                    .Select(kv => new ExecutorRunStatus(kv.Key, kv.Value))
                    .ToList(),
                RecentTransitions: new List<string>(RecentTransitions),
                UpdatedAt: DateTimeOffset.UtcNow);

        /// <summary>从任务簿记投影生成运行状态根（TaskKey/UserInput/ThreadId/InputKey/Status 复制；确定性核心从零开始）。</summary>
        public static AgentLoopState FromTaskRecord(TaskRecord record)
        {
            ArgumentNullException.ThrowIfNull(record);
            return new AgentLoopState
            {
                TaskKey = record.TaskKey,
                UserInput = record.UserInput ?? string.Empty,
                ThreadId = record.ThreadId,
                InputKey = record.InputKey,
                Status = record.Status,
            };
        }
    }
}
