using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 三层层级引用管理端口实现（U8）：组合 history / task / session 三个域接地服务。
/// <para>三层实例只依赖 <see cref="IContainerMemberPort"/>，不触碰具体介质。</para>
/// </summary>
public sealed class HierarchyReferenceManager : IContainerMemberPort
{
    private readonly ContainerReferenceGrounding _history;
    private readonly TaskReferenceGrounding _tasks;
    private readonly SessionReferenceGrounding _sessions;

    public HierarchyReferenceManager(
        ContainerReferenceGrounding history,
        TaskReferenceGrounding tasks,
        SessionReferenceGrounding sessions)
    {
        _history = history ?? throw new ArgumentNullException(nameof(history));
        _tasks = tasks ?? throw new ArgumentNullException(nameof(tasks));
        _sessions = sessions ?? throw new ArgumentNullException(nameof(sessions));
    }

    public Task EnsureContainerAsync(string historyId, CancellationToken ct = default)
        => _history.EnsureHistoryMirrorAsync(historyId, ct);

    public Task<bool> DeleteContainerAsync(string historyId, CancellationToken ct = default)
        => _history.DeleteContainerAsync(historyId, ct);

    public Task AttachMemberAsync(string historyId, string taskKey, CancellationToken ct = default)
        => _history.AttachMemberAsync(historyId, taskKey, ct);

    public Task DetachMemberAsync(string historyId, string taskKey, CancellationToken ct = default)
        => _history.DetachMemberAsync(historyId, taskKey, ct);

    public Task<IReadOnlyList<string>> GetMemberKeysAsync(string historyId, CancellationToken ct = default)
        => _history.GetHistoryTasksAsync(historyId, ct);

    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetMemberKeysManyAsync(
        IReadOnlyCollection<string> historyIds, CancellationToken ct = default)
        => _history.GetHistoryTasksManyAsync(historyIds, ct);

    public Task LinkMemberNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
        => _tasks.EnsureTaskNodeLinkAsync(taskKey, workflowId, nodeKey, ct);

    public Task UnlinkMemberNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
        => _tasks.UnlinkMemberNodeAsync(taskKey, workflowId, nodeKey, ct);

    public Task<IReadOnlyList<string>> GetMemberNodeKeysAsync(string taskKey, string workflowId, CancellationToken ct = default)
        => _tasks.GetMemberNodeKeysAsync(taskKey, workflowId, ct);

    public Task<IReadOnlyList<NodeProjectionItem>> ProjectMemberNodesAsync(string taskKey, string workflowId, CancellationToken ct = default)
        => _tasks.ProjectMemberNodesAsync(taskKey, workflowId, ct);

    public Task<IReadOnlyList<string>> GetContainerNodeKeysAsync(string historyId, string workflowId, CancellationToken ct = default)
        => _history.GetContainerNodeKeysAsync(historyId, workflowId, ct);

    public Task<IReadOnlyList<string>> GetSessionNodeKeysAsync(string sessionName, string workflowId, CancellationToken ct = default)
        => _sessions.GetSessionNodeKeysAsync(sessionName, workflowId, ct);

    public Task<IReadOnlyList<NodeProjectionItem>> ProjectSessionNodesAsync(string sessionName, string workflowId, CancellationToken ct = default)
        => _sessions.ProjectSessionNodesAsync(sessionName, workflowId, ct);
}
