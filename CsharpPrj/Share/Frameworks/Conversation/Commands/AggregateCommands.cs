using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Share.Conversation.Aggregates;
using Share.Conversation.Ids;

namespace Share.Conversation.Commands;

/// <summary>显式创建历史根（只建根，不建 task；是否建 HistoryRoot 会话由命令处理器按策略决定）。</summary>
public sealed record CreateHistoryCommand(
    HistoryKey Key,
    string? Name,
    HistoryKind Kind,
    CreationIntent Intent,
    string Actor,
    bool CreateRootSession = true);

/// <summary>把已存在 task 归拢到已存在历史根（M:N；不隐式建任一端）。</summary>
public sealed record AttachTaskCommand(
    HistoryKey History,
    TaskKey Task,
    CreationIntent Intent,
    string Actor);

/// <summary>解除 task 与历史根的归拢边（只删边）。</summary>
public sealed record DetachTaskCommand(
    HistoryKey History,
    TaskKey Task,
    CreationIntent Intent,
    string Actor);

/// <summary>显式创建任务；ParentHistories 为空 = 孤立 task（合法）。</summary>
public sealed record CreateTaskCommand(
    TaskKey Key,
    IReadOnlyList<HistoryKey> ParentHistories,
    SessionRole Role,
    string? SubAgentTag,
    string? ExecutorId,
    CreationIntent Intent,
    string Actor,
    string? Input = null);

/// <summary>发送消息：只消费既有 history/task/session，不含任何创建字段。</summary>
public sealed record SendMessageCommand(
    SessionKey Session,
    SessionOwner Owner,
    string Agent,
    string Text,
    CancellationToken Token);
