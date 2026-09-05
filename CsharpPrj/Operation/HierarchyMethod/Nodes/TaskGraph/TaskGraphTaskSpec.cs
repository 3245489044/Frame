using Share.HierarchySupport;

namespace Operation.HierarchyMethod.Nodes.TaskGraph;

/// <summary>
/// 任务图入队规格 —— 「创建 task + 维护引用关系」复合服务（Agent.Reference.TaskGraphService）的任务入队契约。
/// 纯 Agent.Contract 承载（不含 AgentTaskItem 具体类），由 Agent.Core 适配器映射为 <c>AgentTaskItem</c> 入队
/// （解耦引用域服务与任务池宿主，保持 Agent.Core → Agent.Reference 单向依赖无环）。
/// </summary>
public sealed record TaskGraphTaskSpec(
    string TaskKey,
    string? UserInput = null,
    string? ThreadId = null,
    string? InputKey = null,
    int Priority = 0,
    int MaxRetries = 0,
    SchedulingMode? SchedulingMode = null,
    string? SchedulerRulesKey = null);
