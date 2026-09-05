using System;
using System.Collections.Generic;
using System.Threading;
using Operation.HierarchyMethod.Nodes;
using System.Linq;
using System.Threading.Tasks;
using Agent.Core.TaskPool;
using Agent.Session;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace Agent.Core.Tests;

/// <summary>历史主循环单测（P1/R3：读 history 视图 → 混合调度决策 → 纯逻辑编排落地，零 LLM）。</summary>
[TestClass]
public class HistoryMainLoopTests
{
    [TestMethod]
    public async Task RunAsync_Enacts_CreateAttachActivate_UntilDone()
    {
        var orch = new FakeHistoryLoopOrchestrator();
        var decider = new QueueHistoryLoopDecider(
            new TaskOrchestrationRequest(TaskOrchestrationAction.CreateTask, "task1", Input: "i1"),
            new TaskOrchestrationRequest(TaskOrchestrationAction.AttachNode, "task1", NodeKey: "nodeA"),
            new TaskOrchestrationRequest(TaskOrchestrationAction.ActivateTask, "task1"));
        var loop = new HistoryMainLoop(new FakeHistoryLoopContext(), orch, decider);

        var result = await loop.RunAsync("h1", "agent-round");

        Assert.AreEqual(3, result.ActionsEnacted);
        CollectionAssert.AreEqual(new[] { "Create", "Attach", "Activate" }, orch.Calls.ToList());
        Assert.AreEqual("h1", orch.LastHistoryId, "CreateTask 经 history 容器归属");
    }

    [TestMethod]
    public async Task RunAsync_Harvests_Nodes_WhenDecided()
    {
        var orch = new FakeHistoryLoopOrchestrator();
        orch.NodesHarvested = 5;
        var decider = new QueueHistoryLoopDecider(
            new TaskOrchestrationRequest(TaskOrchestrationAction.HarvestNodes, ""));
        var loop = new HistoryMainLoop(new FakeHistoryLoopContext(), orch, decider);

        var result = await loop.RunAsync("h1", "agent-round");

        Assert.AreEqual(1, result.ActionsEnacted);
        Assert.AreEqual(5, result.NodesHarvested);
    }

    [TestMethod]
    public async Task RunAsync_EmptyDecisions_ReturnsZero()
    {
        var loop = new HistoryMainLoop(new FakeHistoryLoopContext(), new FakeHistoryLoopOrchestrator(), new QueueHistoryLoopDecider());
        var result = await loop.RunAsync("h1", "agent-round");
        Assert.AreEqual(0, result.ActionsEnacted);
    }

    [TestMethod]
    public async Task RunAsync_ForwardsWorkflowId_ToContext()
    {
        var context = new FakeHistoryLoopContext();
        var orch = new FakeHistoryLoopOrchestrator();
        var loop = new HistoryMainLoop(context, orch, new QueueHistoryLoopDecider(
            new TaskOrchestrationRequest(TaskOrchestrationAction.ActivateTask, "task1")));

        await loop.RunAsync("h1", "wf-custom");

        Assert.AreEqual("wf-custom", context.LastWorkflowId, "主循环应按其运行工作流构建 history 视图（E4）");
    }
}


// ---- 历史主循环测试用内存假端口 ----

internal sealed class FakeHistoryLoopContext : IStandardContextProvider
{
    public string? LastWorkflowId;

    public StandardScope DefaultScopeForRole(StandardContextRole role)
        => role == StandardContextRole.MainLoop ? StandardScope.History : StandardScope.Task;

    public Task<StandardContext> BuildForRoleAsync(StandardContextRole role, string key, string? workflowId = null, CancellationToken ct = default)
    {
        LastWorkflowId = workflowId;
        return Task.FromResult(StandardContext.Empty(DefaultScopeForRole(role), key, workflowId));
    }

    public Task<StandardContext> BuildForScopeAsync(StandardScope scope, string key, string? workflowId = null, CancellationToken ct = default)
        => Task.FromResult(StandardContext.Empty(scope, key, workflowId));
}

internal sealed class QueueHistoryLoopDecider : IHistoryLoopDecider
{
    private readonly Queue<TaskOrchestrationRequest?> _queue;

    public QueueHistoryLoopDecider(params TaskOrchestrationRequest?[] requests)
        => _queue = new Queue<TaskOrchestrationRequest?>(requests);

    public Task<TaskOrchestrationRequest?> NextAsync(StandardContext historyView, int enactedCount, CancellationToken ct = default)
        => Task.FromResult(_queue.Count == 0 ? null : _queue.Dequeue());

    public void OnEnacted(TaskOrchestrationRequest request) { }
}

internal sealed class FakeHistoryLoopOrchestrator : ITaskOrchestrator
{
    public readonly List<string> Calls = new();
    public string? LastHistoryId;
    public int NodesHarvested;

    public Task CreateTaskAsync(string historyId, string taskKey, string? input = null, CancellationToken ct = default) { LastHistoryId = historyId; Calls.Add("Create"); return Task.CompletedTask; }
    public Task ActivateTaskAsync(string taskKey, CancellationToken ct = default) { Calls.Add("Activate"); return Task.CompletedTask; }
    public Task PauseTaskAsync(string taskKey, CancellationToken ct = default) { Calls.Add("Pause"); return Task.CompletedTask; }
    public Task ResumeTaskAsync(string taskKey, CancellationToken ct = default) { Calls.Add("Resume"); return Task.CompletedTask; }
    public Task TerminateTaskAsync(string taskKey, CancellationToken ct = default) { Calls.Add("Terminate"); return Task.CompletedTask; }
    public Task<IReadOnlyList<NodeOutput>> HarvestNodesAsync(string workflowId, CancellationToken ct = default)
    {
        Calls.Add("Harvest");
        var list = new List<NodeOutput>();
        for (var i = 0; i < NodesHarvested; i++) list.Add(new NodeOutput { NodeKey = $"n{i}", WorkflowId = workflowId });
        return Task.FromResult<IReadOnlyList<NodeOutput>>(list);
    }
    public Task AttachNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default) { Calls.Add("Attach"); return Task.CompletedTask; }
}
