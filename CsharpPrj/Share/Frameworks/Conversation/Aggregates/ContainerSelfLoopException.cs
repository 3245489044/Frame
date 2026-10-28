using System;

namespace Share.Conversation.Aggregates;

/// <summary>
/// 历史归拢自环拒绝（history == task）——唯一错误源。命令层不重复判断，
/// 由写边守卫 <c>ReferenceGrounding.AttachHistoryTaskAsync</c> 抛出，上层按此类型映射统一诊断码。
/// </summary>
public sealed class HistorySelfLoopException : InvalidOperationException
{
    public const string DiagnosticCode = "HISTORY_SELF_LOOP_REJECTED";

    public string HistoryKey { get; }
    public string TaskKey { get; }

    public HistorySelfLoopException(string historyKey, string taskKey)
        : base($"{DiagnosticCode}: history_owns_task 禁止自环（history={historyKey}, task={taskKey}）。")
    {
        HistoryKey = historyKey;
        TaskKey = taskKey;
    }
}
