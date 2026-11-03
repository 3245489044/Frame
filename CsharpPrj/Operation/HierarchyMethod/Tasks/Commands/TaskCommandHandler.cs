using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.HierarchyMethod.Tasks;
using Share.Conversation.Aggregates;
using Share.Conversation.Commands;
using Share.Conversation.Ids;
using Share.Conversation.Ports;
using Share.Foundations.Diagnostics;
using Share.TaskStatusPool.TaskPool;

namespace Operation.HierarchyMethod.Tasks.Commands;

/// <summary>
/// 任务命令唯一实现：复用已有成员 → 建 task 簿记 → 显式归拢父历史。
/// 孤立 task（ParentHistories 为空）合法，不写任何 history 边。
/// <para>
/// 归拢统一经 <see cref="ITaskHistoryCommandPort"/>（单父预检 / 自环 / 已归拢带现父，判据单源）；
/// 创建期多父走 <see cref="ITaskHistoryCommandPort.AttachForCreationAsync"/>（显式分模式，不绕过单父判据）。
/// 归属预检在落库**前**完成：已归拢到请求集之外的父 ⇒ 不建 task、结构化拒绝。
/// </para>
/// </summary>
public sealed class TaskCommandHandler : ITaskCommandPort
{
    private readonly ITaskLifecyclePort _lifecycle;
    private readonly Func<ITaskHistoryCommandPort?> _history;
    private readonly ITaskQuery? _query;
    private readonly ISessionStore? _sessions;
    private readonly IConversationLogRootStore? _roots;
    private readonly IDiagnosticsSink? _diagnostics;

    public TaskCommandHandler(
        ITaskLifecyclePort lifecycle,
        Func<ITaskHistoryCommandPort?>? history = null,
        ITaskQuery? query = null,
        ISessionStore? sessions = null,
        IConversationLogRootStore? roots = null,
        IDiagnosticsSink? diagnostics = null)
    {
        _lifecycle = lifecycle ?? throw new ArgumentNullException(nameof(lifecycle));
        _history = history ?? (static () => null);
        _query = query;
        _sessions = sessions;
        _roots = roots;
        _diagnostics = diagnostics;
    }

    public async Task<TaskKey> CreateAsync(CreateTaskCommand cmd, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(cmd);
        if (cmd.Key.IsEmpty) throw new ArgumentException("taskKey 不能为空", nameof(cmd));
        var parents = cmd.ParentHistories ?? Array.Empty<ContainerKey>();

        // 先在**不落库**的前提下校验全部 parent（root 存在 + 非自环），避免“task 已建、attach 失败”的幽灵实体。
        await PreflightParentsAsync(cmd.Key, parents, ct).ConfigureAwait(false);

        // 归拢端口必须在建 task 前可解析：缺失即拒绝（不建出无人归拢的幽灵 task）。
        var historyPort = parents.Count > 0 ? _history() : null;
        if (parents.Count > 0 && historyPort is null)
        {
            throw new InvalidOperationException(
                "（归拢命令未装配）建任务带父需 ITaskHistoryCommandPort：拒绝建 task，避免产生未归拢幽灵成员。");
        }

        // 归属预检（结构判据 = 反向成员查询真源）：现父不在请求集 ⇒ 拒绝且不落库。
        if (_query is not null && parents.Count > 0)
        {
            var owners = await _query.GetHistoriesAsync(cmd.Key, ct).ConfigureAwait(false);
            var foreign = owners
                .Where(owner => !parents.Any(p => SameHistory(p.Value, owner.Value)))
                .Select(static owner => owner.Value)
                .FirstOrDefault();
            if (!string.IsNullOrEmpty(foreign))
            {
                throw new TaskHistoryAttachException(TaskHistoryResult.Rejected(
                    "history.already-owned",
                    $"（已归拢）task={cmd.Key.Value} 已属于历史 {foreign}，拒绝改挂到 {DescribeParents(parents)}（需先显式解除归拢）",
                    foreign));
            }
        }

        // 成员复用（ERR-7/ERR-8）：单父且至少一个身份字段非空才做元数据复用；
        // 多父只复用“已同时属于全部 parent”的同一 key，避免第一个 parent 命中就覆盖其它 parent 的既有成员。
        if (_query is not null)
        {
            if (parents.Count == 0 && await _query.ExistsAsync(cmd.Key, ct).ConfigureAwait(false))
                return cmd.Key;

            if (parents.Count == 1 && (!string.IsNullOrWhiteSpace(cmd.SubAgentTag) || !string.IsNullOrWhiteSpace(cmd.ExecutorId)))
            {
                var existing = await _query.FindMemberAsync(parents[0], cmd.SubAgentTag, cmd.ExecutorId, ct).ConfigureAwait(false);
                if (existing is { } hit) return hit;
            }
            else if (parents.Count > 1 && await _query.ExistsAsync(cmd.Key, ct).ConfigureAwait(false))
            {
                var all = true;
                foreach (var parent in parents)
                {
                    var members = await _query.GetMembersAsync(parent, ct).ConfigureAwait(false);
                    if (!members.Any(m => string.Equals(m.Value, cmd.Key.Value, StringComparison.Ordinal)))
                    {
                        all = false;
                        break;
                    }
                }
                if (all) return cmd.Key;
            }
        }

        await _lifecycle.CreateTaskAsync(cmd.Key.Value, cmd.Input, cmd.SubAgentTag, cmd.ExecutorId, ct)
            .ConfigureAwait(false);

        if (_sessions is not null && SessionKey.TryCreate(cmd.Key.Value, out var sessionKey)
            && await _sessions.GetAsync(sessionKey, ct).ConfigureAwait(false) is null)
        {
            await _sessions.CreateAsync(SessionRecord.Create(
                sessionKey, SessionOwner.OfTask(cmd.Key), cmd.Role,
                cmd.ExecutorId ?? cmd.SubAgentTag ?? "task"), ct).ConfigureAwait(false);
        }

        if (historyPort is not null && parents.Count > 0)
        {
            var result = await historyPort
                .AttachForCreationAsync(cmd.Key.Value, parents.Select(p => p.Value).ToArray(), cmd.Actor, ct)
                .ConfigureAwait(false);
            if (!result.Ok)
            {
                // 失败补偿可见化：task 已落库但未全部归拢 = 显式孤立 task，不伪装成功。
                _diagnostics?.Report("TaskCommand_attach_partial_failed", new
                {
                    task = cmd.Key.Value,
                    parents = parents.Select(p => p.Value).ToArray(),
                    code = result.Code,
                    currentParent = result.CurrentParent,
                    detail = result.Detail,
                });
                throw new TaskHistoryAttachException(result);
            }
        }

        return cmd.Key;
    }

    private async Task PreflightParentsAsync(TaskKey task, IReadOnlyList<ContainerKey> parents, CancellationToken ct)
    {
        foreach (var parent in parents)
        {
            if (parent.IsEmpty)
                throw new ArgumentException("parentHistory 不能为空", nameof(parents));
            if (string.Equals(parent.Value, task.Value, StringComparison.Ordinal))
                throw new ContainerSelfLoopException(parent.Value, task.Value);
            if (_roots is not null && !await _roots.ExistsAsync(parent, ct).ConfigureAwait(false))
                throw new InvalidOperationException($"父历史不存在：{parent.Value}（禁止隐式建根）");
        }
    }

    private static string DescribeParents(IReadOnlyList<ContainerKey> parents)
        => parents.Count == 1 ? parents[0].Value : string.Join(",", parents.Select(static p => p.Value));

    private static bool SameHistory(string left, string right)
        => string.Equals(
            Modules.Node.Identity.NodeEndpointIds.NormalizeHistoryId(left),
            Modules.Node.Identity.NodeEndpointIds.NormalizeHistoryId(right),
            StringComparison.Ordinal);
}

/// <summary>创建期归拢的结构化拒绝（判定载体 = <see cref="TaskHistoryResult"/>；异常只作携带面）。</summary>
public sealed class TaskHistoryAttachException : InvalidOperationException
{
    public TaskHistoryAttachException(TaskHistoryResult result)
        : base($"{result.Code}：{result.Detail}")
        => Result = result ?? throw new ArgumentNullException(nameof(result));

    /// <summary>结构化归拢结果（Code / Detail / CurrentParent）。</summary>
    public TaskHistoryResult Result { get; }
}
