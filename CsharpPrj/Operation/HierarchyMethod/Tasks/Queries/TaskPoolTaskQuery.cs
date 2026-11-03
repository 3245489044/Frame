using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.Container;
using Share.Conversation.Ids;
using Share.Conversation.Ports;
using Share.TaskStatusPool.TaskPool;

namespace Operation.HierarchyMethod.Tasks.Queries;

/// <summary>条目身份（成员复用判据）：宿主经选择器提供，通用查询不认具体条目类型。</summary>
public readonly record struct TaskPoolMemberIdentity(string? SubAgentTag, string? ExecutorId);

/// <summary>
/// 任务查询通用实现：成员枚举走 history_owns_task 边，成员复用按
/// (history, subAgentTag, executorId) 命中任务载荷；孤立 task = 无任何 history 入边。
/// <para>条目字段经 <c>identity</c> 选择器读取（宿主载荷语义不外泄到框架）。</para>
/// </summary>
public sealed class TaskPoolTaskQuery<TItem> : ITaskQuery where TItem : ITaskPoolItem
{
    private readonly ITaskPoolPersistence<TItem> _persistence;
    private readonly IContainerRelationManager? _relations;
    private readonly IConversationLogRootStore? _roots;
    private readonly Func<TItem, TaskPoolMemberIdentity> _identity;

    public TaskPoolTaskQuery(
        ITaskPoolPersistence<TItem> persistence,
        Func<TItem, TaskPoolMemberIdentity> identity,
        IContainerRelationManager? relations = null,
        IConversationLogRootStore? roots = null)
    {
        _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        _identity = identity ?? throw new ArgumentNullException(nameof(identity));
        _relations = relations;
        _roots = roots;
    }

    public async Task<bool> ExistsAsync(TaskKey key, CancellationToken ct = default)
        => !key.IsEmpty && await _persistence.GetAsync(key.Value, ct).ConfigureAwait(false) is not null;

    public async Task<IReadOnlyList<TaskKey>> GetMembersAsync(ContainerKey history, CancellationToken ct = default)
    {
        // 未装配关系端口 ⇒ 不可判定（抛），不得用空集合冒充「无成员」。
        if (_relations is null) throw new TaskQueryUnavailableException(nameof(IContainerRelationManager));
        if (history.IsEmpty) return Array.Empty<TaskKey>();
        var keys = await _relations.GetTaskKeysAsync(history.Value, ct).ConfigureAwait(false);
        return keys.Select(TaskKey.Create).ToList();
    }

    public async Task<IReadOnlyList<ContainerKey>> GetHistoriesAsync(TaskKey task, CancellationToken ct = default)
    {
        if (task.IsEmpty) return Array.Empty<ContainerKey>();
        if (_relations is null) throw new TaskQueryUnavailableException(nameof(IContainerRelationManager));
        var keys = await _relations.GetHistoryKeysAsync(task.Value, ct).ConfigureAwait(false);
        return keys
            .Select(Modules.Node.Identity.NodeEndpointIds.NormalizeHistoryId)
            .Where(k => !string.IsNullOrWhiteSpace(k))
            .Select(ContainerKey.Create)
            .ToList();
    }

    public async Task<TaskKey?> FindMemberAsync(ContainerKey history, string? subAgentTag, string? executorId, CancellationToken ct = default)
    {
        // 无任何身份字段时不做“空 == 空”的过宽匹配。
        if (string.IsNullOrWhiteSpace(subAgentTag) && string.IsNullOrWhiteSpace(executorId)) return null;

        var members = await GetMembersAsync(history, ct).ConfigureAwait(false);
        var items = await _persistence.GetManyAsync(members.Select(m => m.Value).ToList(), ct).ConfigureAwait(false);
        foreach (var item in items)
        {
            var identity = _identity(item);
            if (!string.Equals(identity.SubAgentTag ?? string.Empty, subAgentTag ?? string.Empty, StringComparison.Ordinal)) continue;
            if (!string.Equals(identity.ExecutorId ?? string.Empty, executorId ?? string.Empty, StringComparison.Ordinal)) continue;
            return TaskKey.Create(item.TaskKey);
        }
        return null;
    }

    public async Task<IReadOnlyList<TaskKey>> ListAsync(bool includeMembers, bool includeStandalone, CancellationToken ct = default)
    {
        if (_roots is null || _relations is null)
            throw new TaskQueryUnavailableException(_roots is null ? nameof(IConversationLogRootStore) : nameof(IContainerRelationManager));

        // 一次枚举 root/member 构建反向成员表，再与任务键全集做集合运算。
        var membership = await BuildMembershipMapAsync(ct).ConfigureAwait(false);
        var keys = await _persistence.ListKeysAsync(int.MaxValue, ct).ConfigureAwait(false);
        var result = new List<TaskKey>();
        foreach (var taskKey in keys)
        {
            var key = TaskKey.Create(taskKey);
            var isMember = membership.ContainsKey(key.Value);
            if ((isMember && includeMembers) || (!isMember && includeStandalone)) result.Add(key);
        }
        return result;
    }

    private async Task<Dictionary<string, List<ContainerKey>>> BuildMembershipMapAsync(CancellationToken ct)
    {
        var map = new Dictionary<string, List<ContainerKey>>(StringComparer.Ordinal);
        var roots = await _roots!.ListRootsAsync(ct).ConfigureAwait(false);
        var rootKeys = roots
            .Where(r => !r.Key.IsEmpty)
            .Select(r => r.Key.Value)
            .Distinct(StringComparer.Ordinal)
            .ToList();
        if (rootKeys.Count == 0) return map;

        var membersByRoot = await _relations!.GetTaskKeysManyAsync(rootKeys, ct).ConfigureAwait(false);
        foreach (var rootKey in rootKeys)
        {
            if (!membersByRoot.TryGetValue(rootKey, out var members) || members is null) continue;
            var root = ContainerKey.Create(rootKey);
            foreach (var member in members)
            {
                if (string.IsNullOrWhiteSpace(member)) continue;
                if (!map.TryGetValue(member, out var list))
                {
                    list = new List<ContainerKey>();
                    map[member] = list;
                }
                if (!list.Contains(root)) list.Add(root);
            }
        }
        return map;
    }
}

