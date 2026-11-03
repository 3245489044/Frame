using System.Collections.Generic;
using Share.HierarchySupport;

namespace Operation.HierarchyMethod.Tasks.TaskGraph;

/// <summary>
/// 任务图入队规格 —— 「创建 task + 维护引用关系」复合服务的任务入队契约。
/// 纯通用层承载（<c>Operation.HierarchyMethod.Tasks.TaskGraph</c>，不含 <c>AgentTaskItem</c> 具体类），
/// 由 Agent.Core 适配器映射为 <c>AgentTaskItem</c> 入队（解耦引用域服务与任务池宿主）。
/// </summary>
public sealed record TaskGraphTaskSpec(
    string TaskKey,
    string? UserInput = null,
    string? ThreadId = null,
    string? InputKey = null,
    int Priority = 0,
    int MaxRetries = 0,
    SchedulingMode? SchedulingMode = null,
    string? SchedulerRulesKey = null,
    string? SubAgentTag = null,
    string? ExecutorId = null,
    string? HistoryId = null,
    IReadOnlyList<string>? RelatedNodes = null,
    bool? Active = null);
