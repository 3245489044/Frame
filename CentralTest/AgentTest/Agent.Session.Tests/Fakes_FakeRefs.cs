using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;

namespace Agent.Session.Tests;

internal sealed class FakeRefs : IHierarchyReferenceManager
{
    private readonly Dictionary<string, List<string>> _historyTasks = new();
    private readonly Dictionary<string, List<string>> _taskNodes = new();

    public void AddHistoryTask(string historyId, string taskKey) => Add(_historyTasks, historyId, taskKey);
    public void AddTaskNode(string taskKey, string workflowId, string nodeKey) => Add(_taskNodes, taskKey, nodeKey);

    private static void Add(Dictionary<string, List<string>> map, string key, string value)
    {
        if (!map.TryGetValue(key, out var list)) map[key] = list = new List<string>();
        if (!list.Contains(value)) list.Add(value);
    }

    public Task<IReadOnlyList<string>> GetHistoryTaskKeysAsync(string historyId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(_historyTasks.TryGetValue(historyId, out var l) ? l.ToList() : new List<string>());
    public Task<IReadOnlyList<string>> GetTaskNodeKeysAsync(string taskKey, string workflowId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(_taskNodes.TryGetValue(taskKey, out var l) ? l.ToList() : new List<string>());
    public Task<IReadOnlyList<string>> GetHistoryNodeKeysAsync(string historyId, string workflowId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(new List<string>());

    public Task EnsureHistoryAsync(string historyId, CancellationToken ct = default) => Task.CompletedTask;
    public Task<bool> DeleteHistoryAsync(string historyId, CancellationToken ct = default) { _historyTasks.Remove(historyId); return Task.FromResult(true); }
    public Task AttachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct = default) { AddHistoryTask(historyId, taskKey); return Task.CompletedTask; }
    public Task DetachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct = default) { if (_historyTasks.TryGetValue(historyId, out var l)) l.Remove(taskKey); return Task.CompletedTask; }
    public Task LinkTaskNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default) { AddTaskNode(taskKey, workflowId, nodeKey); return Task.CompletedTask; }
    public Task UnlinkTaskNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default) { if (_taskNodes.TryGetValue(taskKey, out var l)) l.Remove(nodeKey); return Task.CompletedTask; }
}
