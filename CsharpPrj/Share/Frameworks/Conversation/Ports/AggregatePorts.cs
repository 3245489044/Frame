using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Share.Conversation.Aggregates;
using Share.Conversation.Commands;
using Share.Conversation.Ids;

// 说明：本文件是聚合端口唯一声明面（root/session/transcript/history/task/message/gate/query）。

namespace Share.Conversation.Ports;

/// <summary>历史根注册表只读/写端口（真源 = ur_histories，只存根，不存日志）。</summary>
public interface IConversationLogRootStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<IReadOnlyList<ContainerRoot>> ListRootsAsync(CancellationToken ct = default);
    Task<bool> ExistsAsync(ContainerKey key, CancellationToken ct = default);
    Task<ContainerRoot?> GetAsync(ContainerKey key, CancellationToken ct = default);
    Task CreateAsync(ContainerRoot root, CancellationToken ct = default);
    Task<bool> DeleteAsync(ContainerKey key, CancellationToken ct = default);
}

/// <summary>会话实例注册端口（真源 = ur_sessions）。</summary>
public interface ISessionStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<SessionRecord?> GetAsync(SessionKey key, CancellationToken ct = default);
    Task<IReadOnlyList<SessionRecord>> ListByOwnerAsync(SessionOwner owner, CancellationToken ct = default);
    Task CreateAsync(SessionRecord session, CancellationToken ct = default);
    Task<bool> DeleteAsync(SessionKey key, CancellationToken ct = default);
}

/// <summary>会话日志端口（真源 = ur_session_logs；append-only 投影 + watermark）。</summary>
public interface ISessionTranscriptStore
{
    Task InitializeAsync(CancellationToken ct = default);
    Task<ConversationLog?> GetAsync(SessionKey key, CancellationToken ct = default);
    Task AppendProjectionAsync(SessionKey key, ConversationLog log, long watermark, CancellationToken ct = default);
    Task<bool> DeleteAsync(SessionKey key, CancellationToken ct = default);

    /// <summary>消息数（空实体判定/统计用）；缺省实现反序列化整条日志，存储实现宜覆写为下推。</summary>
    async Task<int> CountMessagesAsync(SessionKey key, CancellationToken ct = default)
    {
        var log = await GetAsync(key, ct).ConfigureAwait(false);
        return log?.Messages.Count ?? 0;
    }

    /// <summary>日志是否为空（无消息）；缺省实现经 <see cref="CountMessagesAsync"/>。</summary>
    async Task<bool> IsEmptyAsync(SessionKey key, CancellationToken ct = default)
        => await CountMessagesAsync(key, ct).ConfigureAwait(false) == 0;
}

/// <summary>历史命令端口（唯一创建/归拢/删除入口）。</summary>
public interface IContainerCommandPort
{
    Task<ContainerRoot> CreateAsync(CreateContainerCommand cmd, CancellationToken ct = default);
    Task AttachTaskAsync(AttachTaskCommand cmd, CancellationToken ct = default);
    Task DetachTaskAsync(DetachTaskCommand cmd, CancellationToken ct = default);
    Task<bool> DeleteAsync(DeleteContainerCommand cmd, CancellationToken ct = default);
}

/// <summary>
/// 历史根目录查询（唯一枚举入口）—— 只认 root store（根注册表行）；
/// 过滤/去重/排序全部在本实现内完成，消费方不得再写第二份根枚举逻辑。
/// </summary>
public interface IContainerRootCatalog
{
    Task<IReadOnlyList<ContainerRoot>> ListAsync(CancellationToken ct = default);
    Task<bool> ExistsAsync(ContainerKey key, CancellationToken ct = default);
}

/// <summary>任务命令端口（Agent 侧实现；Frame 只定义契约）。</summary>
public interface ITaskCommandPort
{
    Task<TaskKey> CreateAsync(CreateTaskCommand cmd, CancellationToken ct = default);
}

/// <summary>消息命令端口（只消费既有实体）。</summary>
public interface IMessageCommandPort
{
    Task SendAsync(SendMessageCommand cmd, CancellationToken ct = default);
}

/// <summary>创建授权端口（最终判据；提示词只作提议）。</summary>
public interface ICreationGate
{
    CreationDecision AuthorizeHistory(CreationIntent intent, string actor);
    CreationDecision AuthorizeTask(
        CreationIntent intent,
        IReadOnlyList<ContainerKey> parents,
        SessionRole role,
        string? subAgentTag,
        string? executorId,
        string? agent);
}

/// <summary>任务查询端口（M:N 成员/反向归属/成员复用）。</summary>
public interface ITaskQuery
{
    Task<bool> ExistsAsync(TaskKey key, CancellationToken ct = default);
    Task<IReadOnlyList<TaskKey>> GetMembersAsync(ContainerKey history, CancellationToken ct = default);
    Task<IReadOnlyList<ContainerKey>> GetHistoriesAsync(TaskKey task, CancellationToken ct = default);
    Task<TaskKey?> FindMemberAsync(ContainerKey history, string? subAgentTag, string? executorId, CancellationToken ct = default);
    Task<IReadOnlyList<TaskKey>> ListAsync(bool includeMembers, bool includeStandalone, CancellationToken ct = default);
}
