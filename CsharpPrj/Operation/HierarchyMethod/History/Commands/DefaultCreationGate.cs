using System;
using System.Collections.Generic;
using Share.Conversation.Aggregates;
using Share.Conversation.Ids;
using Share.Conversation.Ports;

namespace Operation.HierarchyMethod.History.Commands;

/// <summary>
/// 默认创建门禁（Frame 通用规则，零 Agent 词）：
/// Automatic 一律拒绝；TaskMember 必须有父历史；HistoryRoot session 不得作为 task 创建；
/// TaskMember 会话以 history 身份再分发（agent=history）拒绝。
/// Agent 可在外层装饰/替换本实现，但不得绕过命令服务内部调用。
/// </summary>
public sealed class DefaultCreationGate : ICreationGate
{
    public CreationDecision AuthorizeHistory(CreationIntent intent, string actor)
    {
        if (intent == CreationIntent.Automatic)
            return CreationDecision.Reject("automatic-intent-cannot-create-history");
        return CreationDecision.Allow("history-create-authorized");
    }

    public CreationDecision AuthorizeTask(
        CreationIntent intent,
        IReadOnlyList<HistoryKey> parents,
        SessionRole role,
        string? subAgentTag,
        string? executorId,
        string? agent)
    {
        if (intent == CreationIntent.Automatic)
            return CreationDecision.Reject("automatic-intent-cannot-create-task");
        if (role == SessionRole.HistoryRoot)
            return CreationDecision.Reject("history-root-role-cannot-create-task");
        if (role == SessionRole.TaskMember && (parents is null || parents.Count == 0))
            return CreationDecision.Reject("task-member-requires-parent-history");
        if (role == SessionRole.TaskMember && string.Equals(agent, "history", StringComparison.OrdinalIgnoreCase))
            return CreationDecision.Reject("task-member-session-cannot-dispatch-as-history");
        return CreationDecision.Allow("task-create-authorized");
    }
}
