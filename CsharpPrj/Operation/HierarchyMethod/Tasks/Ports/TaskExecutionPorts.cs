using Operation.HierarchyMethod.Tasks.Loops;
using Share.HierarchySupport;

namespace Operation.HierarchyMethod.Tasks.Ports
{
    /// <summary>
    /// 任务执行器（框架级最小契约，LLM-free）—— 一个 `ExecutionNode` 的执行单元句柄。
    /// 框架本体不定义具体执行器；Agent 执行器（LlmTaskExecutor / RuleTaskExecutor / StatelessAnswerTaskExecutor）实现本契约，
    /// 经 <see cref="ITaskExecutorResolver"/> 注入。执行细节（提示词/轮数/停止规则）经节点 <c>WorkflowConfigKey</c> 外置，不在本契约内。
    /// </summary>
    public interface ITaskExecutor
    {
        /// <summary>执行器句柄 Id（如 "stateless-answer"；与 <c>ExecutionNode.ExecutorId</c> 匹配）。</summary>
        string ExecutorId { get; }

        /// <summary>执行一次（节点携带的载荷为不透明透传；input 为 task 输入）。</summary>
        Task<string> ExecuteAsync(ExecutionNode node, string input, CancellationToken ct);
    }

    /// <summary>
    /// 任务执行器解析端口 —— 按 <see cref="TaskRecord"/> 的 <see cref="ExecutionNode"/> 解析执行器句柄。
    /// 框架与 Agent 的唯一执行耦合面（P3 端口注入）：未注入 / 未命中返回 null → 调用方走确定性路径（P5 可空守卫）。
    /// </summary>
    public interface ITaskExecutorResolver
    {
        /// <summary>解析执行器句柄；未注入 / 未命中返回 null（零 LLM 确定性执行）。</summary>
        ITaskExecutor? Resolve(TaskRecord task);
    }

    /// <summary>
    /// 状态导演（框架级最小契约，LLM-free）—— 决定本轮整体状态变更由谁提议（自主迭代源）。
    /// Agent 实现（LlmStateDirector / ConfigStateDirector / NoopStateDirector）。
    /// </summary>
    public interface IStateDirector
    {
        /// <summary>导演模式（Hybrid=LLM 自由探索 / Config=规则表驱动 / Deterministic=纯规则迭代）。</summary>
        SchedulingMode Mode { get; }
    }

    /// <summary>
    /// 导演提供端口 —— 按 <see cref="AgentLoopState"/> 提供导演（Hybrid 混合调度时）；缺省 = 确定性导演（零 LLM）。
    /// 未注入返回 null → 框架走确定性路径，不消耗 AI 额度。
    /// </summary>
    public interface IStateDirectorProvider
    {
        IStateDirector? Resolve(AgentLoopState state);
    }

    /// <summary>
    /// 工作流配置键解析端口 —— 把 <see cref="ExecutionNode.WorkflowConfigKey"/> 解析为执行所需的配置视图
    /// （提示词计划 / 轮数 / 停止规则等 LLM 细节的外部来源）。框架**不内嵌任何提示词**（P3）；
    /// 实现由 Agent 提供（读取 ur_config / Config/workflows/*.json）。
    /// 未装配 / 未命中 → null（P5 可空守卫，走确定性路径）。
    /// </summary>
    public interface IConfigKeyResolver
    {
        /// <summary>解析工作流配置键；未装配 / 未命中返回 null。</summary>
        ResolvedWorkflowConfig? Resolve(string? workflowConfigKey);
    }

    /// <summary>工作流配置解析视图（LLM-free 轻量 DTO）。提示词本体不在此承载——经 <see cref="ResolvedWorkflowConfig.PromptKey"/> 由消费方按需读取。</summary>
    public sealed record ResolvedWorkflowConfig(
        string? ConfigKey,
        int? MaxRounds = null,
        string? StopRule = null,
        string? PromptKey = null);

}
