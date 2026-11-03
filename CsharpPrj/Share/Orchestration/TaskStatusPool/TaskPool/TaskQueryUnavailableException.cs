using System;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// 任务查询依赖未装配——禁止用空集合冒充“无归属”，否则所有 task 会被误判为孤立。
/// </summary>
public sealed class TaskQueryUnavailableException : InvalidOperationException
{
    public const string DiagnosticCode = "TASK_QUERY_UNAVAILABLE";

    public TaskQueryUnavailableException(string dependency)
        : base($"{DiagnosticCode}: 任务查询依赖未装配（{dependency}）。")
    {
    }
}
