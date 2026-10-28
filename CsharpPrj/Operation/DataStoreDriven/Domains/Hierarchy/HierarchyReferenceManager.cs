using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;

namespace Operation.DataStoreDriven.Core;

/// <summary>
/// 三层层级引用管理端口实现（U8）：组合 history / task / session 三个域接地服务。
/// <para>三层实例只依赖 <see cref="IHierarchyReferenceManager"/>，不触碰具体介质。</para>
/// </summary>
public sealed class HierarchyReferenceManager : IHierarchyReferenceManager
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

    public Task EnsureHistoryAsync(string historyId, CancellationToken ct = default)
        => _history.EnsureHistoryMirrorAsync(historyId, ct);

    public Task<bool> DeleteHistoryAsync(string historyId, CancellationToken ct = default)
        => _history.DeleteHistoryAsync(historyId, ct);

    public Task AttachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct = default)
        => _history.AttachHistoryTaskAsync(historyId, taskKey, ct);

    public Task DetachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct = default)
        => _history.DetachHistoryTaskAsync(historyId, taskKey, ct);

    public Task<IReadOnlyList<string>> GetHistoryTaskKeysAsync(string historyId, CancellationToken ct = default)
        => _history.GetHistoryTasksAsync(historyId, ct);

    public Task<IReadOnlyDictionary<string, IReadOnlyList<string>>> GetHistoryTaskKeysManyAsync(
        IReadOnlyCollection<string> historyIds, CancellationToken ct = default)
        => _history.GetHistoryTasksManyAsync(historyIds, ct);

    public Task LinkTaskNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
        => _tasks.EnsureTaskNodeLinkAsync(taskKey, workflowId, nodeKey, ct);

    public Task UnlinkTaskNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
        => _tasks.UnlinkTaskNodeAsync(taskKey, workflowId, nodeKey, ct);

    public Task<IReadOnlyList<string>> GetTaskNodeKeysAsync(string taskKey, string workflowId, CancellationToken ct = default)
        => _tasks.GetTaskNodeKeysAsync(taskKey, workflowId, ct);

    public Task<IReadOnlyList<NodeProjectionItem>> ProjectTaskNodesAsync(string taskKey, string workflowId, CancellationToken ct = default)
        => _tasks.ProjectTaskNodesAsync(taskKey, workflowId, ct);

    public Task<IReadOnlyList<string>> GetHistoryNodeKeysAsync(string historyId, string workflowId, CancellationToken ct = default)
        => _history.GetHistoryNodeKeysAsync(historyId, workflowId, ct);

    public Task<IReadOnlyList<string>> GetSessionNodeKeysAsync(string sessionName, string workflowId, CancellationToken ct = default)
        => _sessions.GetSessionNodeKeysAsync(sessionName, workflowId, ct);

    public Task<IReadOnlyList<NodeProjectionItem>> ProjectSessionNodesAsync(string sessionName, string workflowId, CancellationToken ct = default)
        => _sessions.ProjectSessionNodesAsync(sessionName, workflowId, ct);
}
