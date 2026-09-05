using Operation.HierarchyMethod.Reference;

namespace Operation.HierarchyMethod.History.Runtime;

/// <summary>
/// history 级任务装配声明 —— 启动该 history 下 task 时的装配模板（History 编排层通用 DTO，零 Agent 依赖）。
/// 启动时由 Agent 适配器（<see cref="IHistoryRuntimePort.StartAsync"/> 实现）消费：选 RoundSpec、
/// 覆盖种子键、选标准上下文粒度、默认子智能体路由与落 task 的执行模式（ExecutionFlag）。
/// 全部字段可空：缺省 = Agent 适配器回退其既有默认行为（向后兼容，零回归）。
/// </summary>
public sealed record HistoryPromptProfile(
    /// <summary>会话工作流 RoundSpec 键（如 session-answer / session-cline / collect-analyze-act / agent-loop）。</summary>
    string? WorkflowKey = null,
    /// <summary>覆盖会话种子键（system / max_rounds / ...），透传会话工作流种子。</summary>
    IReadOnlyDictionary<string, object?>? SeedOverrides = null,
    /// <summary>标准上下文粒度（node | task | history），透传 cline_execute 上下文构建。</summary>
    string? ContextScope = null,
    /// <summary>默认子智能体路由（cline | answer），透传会话 send 的 agent 选择。</summary>
    string? Agent = null,
    /// <summary>标准上下文键（nodeKey / taskKey / historyId），透传 cline_execute 上下文构建。</summary>
    string? ContextKey = null,
    /// <summary>落 task 的执行模式（Auto/Llm/Rule/Deterministic/Std），经执行标识符系统落到 SchedulingMode。</summary>
    ExecutionFlag? FlagTemplate = null);
