using System.Threading;
using Operation.DataStoreDriven.Abstractions;
using Operation.HierarchyMethod.Nodes;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Agent.Core.TaskPool;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Agent.Core.Tests;

/// <summary>主循环编排器单测（P1 重构：纯逻辑、零 LLM——只管理 task 状态 + 编排 nodes）。</summary>
[TestClass]
public class TaskOrchestratorTests
{
    [TestMethod]
    public async Task CreateTask_EnsuresHistory_AttachesHistoryTask_AndCreatesTaskBookkeeping()
    {
        var refs = new FakeOrchestratorRefs();
        var lifecycle = new FakeOrchestratorLifecycle();
        var orch = new TaskOrchestrator(refs, new FakeOrchestratorNodes(), lifecycle);

        await orch.CreateTaskAsync("h1", "task1", "input-1");

        CollectionAssert.AreEqual(new[] { "h1" }, refs.Histories.ToList());
        CollectionAssert.AreEqual(new[] { "task1" }, refs.HistoryTasks["h1"].ToList());
        Assert.AreEqual(("task1", "input-1"), lifecycle.Created);
    }

    [TestMethod]
    public async Task AttachNode_LinksTaskNode_And_HarvestNodes_ReturnsNodeOutputs()
    {
        var refs = new FakeOrchestratorRefs();
        var nodes = new FakeOrchestratorNodes();
        nodes.Put("agent-round", "nodeA", "result", "A产出");
        var orch = new TaskOrchestrator(refs, nodes, new FakeOrchestratorLifecycle());

        await orch.AttachNodeAsync("task1", "agent-round", "nodeA");
        CollectionAssert.AreEqual(new[] { "nodeA" }, refs.TaskNodes["task1"].ToList());

        var outputs = await orch.HarvestNodesAsync("agent-round");
        Assert.AreEqual(1, outputs.Count);
        Assert.AreEqual("nodeA", outputs[0].NodeKey);
    }

    [TestMethod]
    public async Task Lifecycle_Transitions_DelegatedToPort()
    {
        var lifecycle = new FakeOrchestratorLifecycle();
        var orch = new TaskOrchestrator(lifecycle: lifecycle);

        await orch.ActivateTaskAsync("task1");
        await orch.PauseTaskAsync("task1");
        await orch.ResumeTaskAsync("task1");
        await orch.TerminateTaskAsync("task1");

        CollectionAssert.AreEqual(new[] { "Activate", "Pause", "Resume", "Terminate" }, lifecycle.Calls.ToList());
    }

    [TestMethod]
    public async Task UnconfiguredPorts_ReturnNoOp_NoThrow()
    {
        var orch = new TaskOrchestrator(); // 全部端口未装配
        await orch.CreateTaskAsync("h1", "task1");       // no-op
        await orch.ActivateTaskAsync("task1");           // no-op
        await orch.AttachNodeAsync("task1", "wf", "n");  // no-op
        var outputs = await orch.HarvestNodesAsync("wf"); // 空集
        Assert.AreEqual(0, outputs.Count);
    }
}


// ---- 编排器测试用内存假端口 ----

internal sealed class FakeOrchestratorLifecycle : ITaskLifecyclePort
{
    public (string Key, string? Input)? Created;
    public readonly List<string> Calls = new();

    public Task CreateTaskAsync(string taskKey, string? input = null, CancellationToken ct = default) { Created = (taskKey, input); Calls.Add("Create"); return Task.CompletedTask; }
    public Task ActivateTaskAsync(string taskKey, CancellationToken ct = default) { Calls.Add("Activate"); return Task.CompletedTask; }
    public Task PauseTaskAsync(string taskKey, CancellationToken ct = default) { Calls.Add("Pause"); return Task.CompletedTask; }
    public Task ResumeTaskAsync(string taskKey, CancellationToken ct = default) { Calls.Add("Resume"); return Task.CompletedTask; }
    public Task TerminateTaskAsync(string taskKey, CancellationToken ct = default) { Calls.Add("Terminate"); return Task.CompletedTask; }
}

internal sealed class FakeOrchestratorRefs : IHierarchyReferenceManager
{
    public readonly List<string> Histories = new();
    public readonly Dictionary<string, List<string>> HistoryTasks = new();
    public readonly Dictionary<string, List<string>> TaskNodes = new();

    public Task EnsureHistoryAsync(string historyId, CancellationToken ct = default) { if (!Histories.Contains(historyId)) Histories.Add(historyId); return Task.CompletedTask; }
    public Task<bool> DeleteHistoryAsync(string historyId, CancellationToken ct = default) { Histories.Remove(historyId); HistoryTasks.Remove(historyId); return Task.FromResult(true); }
    public Task AttachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct = default) { if (!HistoryTasks.TryGetValue(historyId, out var l)) HistoryTasks[historyId] = l = new List<string>(); if (!l.Contains(taskKey)) l.Add(taskKey); return Task.CompletedTask; }
    public Task DetachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct = default) { if (HistoryTasks.TryGetValue(historyId, out var l)) l.Remove(taskKey); return Task.CompletedTask; }
    public Task<IReadOnlyList<string>> GetHistoryTaskKeysAsync(string historyId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(HistoryTasks.TryGetValue(historyId, out var l) ? l.ToList() : new List<string>());
    public Task LinkTaskNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default) { if (!TaskNodes.TryGetValue(taskKey, out var l)) TaskNodes[taskKey] = l = new List<string>(); if (!l.Contains(nodeKey)) l.Add(nodeKey); return Task.CompletedTask; }
    public Task UnlinkTaskNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default) { if (TaskNodes.TryGetValue(taskKey, out var l)) l.Remove(nodeKey); return Task.CompletedTask; }
    public Task<IReadOnlyList<string>> GetTaskNodeKeysAsync(string taskKey, string workflowId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(TaskNodes.TryGetValue(taskKey, out var l) ? l.ToList() : new List<string>());
    public Task<IReadOnlyList<string>> GetHistoryNodeKeysAsync(string historyId, string workflowId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(new List<string>());
}

internal sealed class FakeOrchestratorNodes : INodeOutputStore
{
    private readonly Dictionary<(string, string), NodeOutput> _outputs = new();

    public void Put(string workflowId, string nodeKey, string port, object? value)
    {
        if (!_outputs.TryGetValue((workflowId, nodeKey), out var o)) _outputs[(workflowId, nodeKey)] = o = new NodeOutput { NodeKey = nodeKey, WorkflowId = workflowId };
        o.OutputPorts[port] = value;
    }

    public Task<NodeOutput?> TryGetNodeOutputAsync(string workflowId, string nodeKey, CancellationToken ct = default) => Task.FromResult(_outputs.TryGetValue((workflowId, nodeKey), out var o) ? o : null);
    public Task<IReadOnlyList<NodeOutput>> EnumerateOutputsAsync(string workflowId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<NodeOutput>>(_outputs.Where(kv => kv.Key.Item1 == workflowId).Select(kv => kv.Value).ToList());
    public Task<IReadOnlyList<string>> EnumerateNodeKeysAsync(string workflowId, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<string>>(_outputs.Where(kv => kv.Key.Item1 == workflowId).Select(kv => kv.Key.Item2).ToList());

    public Task<IReadOnlyList<NodeSignal>> EnumerateSignalsAsync(string workflowId, string? module = null, string? nodeType = null, string? region = null, int limit = -1, CancellationToken ct = default) => Task.FromResult<IReadOnlyList<NodeSignal>>(_outputs.Where(kv => kv.Key.Item1 == workflowId).Select(kv => new NodeSignal(kv.Value.NodeKey, kv.Value.WorkflowId, kv.Value.Module, kv.Value.Region, kv.Value.NodeType, kv.Value.Freshness, kv.Value.ValueScore, kv.Value.OutputDigest, kv.Value.Sticky, kv.Value.Priority)).ToList());

    public Task WriteSignalMetadataAsync(string workflowId, string nodeKey, NodeSignal signal, CancellationToken ct = default) => Task.FromResult(signal);

    public Task InitializeAsync(CancellationToken ct = default) => Task.CompletedTask;
    public Task WriteOutputAsync(string workflowId, string nodeKey, string port, object? value, CancellationToken ct = default) { Put(workflowId, nodeKey, port, value); return Task.CompletedTask; }
    public Task WriteOutputAsync(string workflowId, string nodeKey, string port, object? value, OutputVisibility visibility, CancellationToken ct = default) { Put(workflowId, nodeKey, port, value); return Task.CompletedTask; }
    public Task<NodeOutput?> TryGetNodeOutputScopedAsync(string workflowId, string nodeKey, string requestingWorkflowId, CancellationToken ct = default) => TryGetNodeOutputAsync(workflowId, nodeKey, ct);
    public Task<IReadOnlyList<NodeOutput>> EnumerateOutputsScopedAsync(string workflowId, string requestingWorkflowId, CancellationToken ct = default) => EnumerateOutputsAsync(workflowId, ct);
    public Task ClearNodeAsync(string workflowId, string nodeKey, CancellationToken ct = default) { _outputs.Remove((workflowId, nodeKey)); return Task.CompletedTask; }
    public Task ClearWorkflowAsync(string workflowId, CancellationToken ct = default) { foreach (var k in _outputs.Keys.Where(k => k.Item1 == workflowId).ToList()) _outputs.Remove(k); return Task.CompletedTask; }
}
