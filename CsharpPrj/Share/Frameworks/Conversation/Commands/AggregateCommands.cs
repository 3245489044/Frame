using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Share.Conversation.Aggregates;
using Share.Conversation.Ids;

namespace Share.Conversation.Commands;

/// <summary>显式创建历史根（只建根，不建 task；是否建 ContainerRoot 会话由命令处理器按策略决定）。</summary>
public sealed record CreateContainerCommand(
    ContainerKey Key,
    string? Name,
    ContainerKind Kind,
    CreationIntent Intent,
    string Actor,
    bool CreateRootSession = true);

/// <summary>把已存在 task 归拢到已存在历史根（M:N；不隐式建任一端）。</summary>
public sealed record AttachTaskCommand(
    ContainerKey History,
    TaskKey Task,
    CreationIntent Intent,
    string Actor);

/// <summary>解除 task 与历史根的归拢边（只删边）。</summary>
public sealed record DetachTaskCommand(
    ContainerKey History,
    TaskKey Task,
    CreationIntent Intent,
    string Actor);

/// <summary>删除历史根（聚合自身：图根/边 + 根会话 + 会话日志；不顺带删 task）。</summary>
public sealed record DeleteContainerCommand(
    ContainerKey Key,
    CreationIntent Intent,
    string Actor,
    bool DeleteSessions = true);

/// <summary>显式创建任务；ParentHistories 为空 = 孤立 task（合法）。</summary>
public sealed record CreateTaskCommand(
    TaskKey Key,
    IReadOnlyList<ContainerKey> ParentHistories,
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
