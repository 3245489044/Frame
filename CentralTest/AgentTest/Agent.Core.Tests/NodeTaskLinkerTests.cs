using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.DataStoreDriven.Abstractions;
using global::Operation.HierarchyMethod.Nodes.Linkage;

namespace Agent.Core.Tests;

/// <summary>
/// task→node 边自动建链模块回归护栏（HierarchyMethod.Nodes.Linkage，节点+边为基础）：
/// 幂等、节点键三形态归一、端点镜像保证。该模块被普通会话发送流程（ConversationPipeline）消费，
/// 为任务自动创建 cross_db_link 边，使任务板块/标准上下文能显示其产出节点。
/// </summary>
[TestClass]
public class NodeTaskLinkerTests
{
    [TestMethod]
    public async Task LinkTaskNodesAsync_NormalizesKeys_Dedups_EnsuresMirrorsAndEdges()
    {
        var maintenance = new FakeMaintenance();
        var linker = new NodeTaskLinker(maintenance);

        // 三种键形态（裸 / {wf}.{node} / nodes.{wf}.{node}）+ 重复 → 归一为同一裸键并去重。
        int linked = await linker.LinkTaskNodesAsync(
            "task-1", "", new[] { "source", "agent-round.source", "nodes.agent-round.source", "source" },
            CancellationToken.None);

        Assert.AreEqual(1, linked);
        CollectionAssert.Contains(maintenance.TaskMirrors, "task-1");
        Assert.IsTrue(maintenance.NodeMirrors.Contains(("agent-round", "source")));
        Assert.IsTrue(maintenance.TaskNodeLinks.Contains(("task-1", "agent-round", "source")));
    }

    [TestMethod]
    public async Task LinkTaskNodesAsync_MultipleDistinct_NormalizedByWorkflow()
    {
        var maintenance = new FakeMaintenance();
        var linker = new NodeTaskLinker(maintenance);

        // 空 workflowId → 归一为默认 agent-round；wf.a 归一到 a（同 a 去重）。
        int linked = await linker.LinkTaskNodesAsync(
            "t", null!, new[] { "a", "wf.a", "b" }, CancellationToken.None);

        Assert.AreEqual(2, linked);
        Assert.AreEqual(2, maintenance.TaskNodeLinks.Count);
        Assert.IsTrue(maintenance.TaskNodeLinks.Contains(("t", "agent-round", "a")));
        Assert.IsTrue(maintenance.TaskNodeLinks.Contains(("t", "agent-round", "b")));
    }

    [TestMethod]
    public async Task LinkTaskNodesAsync_EmptyInputs_NoCalls()
    {
        var maintenance = new FakeMaintenance();
        var linker = new NodeTaskLinker(maintenance);

        Assert.AreEqual(0, await linker.LinkTaskNodesAsync("", "agent-round", new[] { "a" }, CancellationToken.None));
        Assert.AreEqual(0, await linker.LinkTaskNodesAsync("t", "agent-round", Array.Empty<string>(), CancellationToken.None));
        Assert.AreEqual(0, await linker.LinkTaskNodesAsync("t", "agent-round", null!, CancellationToken.None));
        Assert.AreEqual(0, maintenance.TaskMirrors.Count);
        Assert.AreEqual(0, maintenance.TaskNodeLinks.Count);
    }

    /// <summary>记录三路写原语（镜像/建边），其余未使用方法抛 NotImplementedException（本测试不触达）。</summary>
    private sealed class FakeMaintenance : IReferenceMaintenance
    {
        public List<string> TaskMirrors { get; } = new();
        public List<(string Wf, string Node)> NodeMirrors { get; } = new();
        public List<(string Task, string Wf, string Node)> TaskNodeLinks { get; } = new();

        public Task EnsureTaskMirrorAsync(string taskKey, CancellationToken ct = default)
        { TaskMirrors.Add(taskKey); return Task.CompletedTask; }

        public Task EnsureNodeMirrorAsync(string workflowId, string nodeKey, string? symbol = null, CancellationToken ct = default)
        { NodeMirrors.Add((workflowId, nodeKey)); return Task.CompletedTask; }

        public Task EnsureTaskNodeLinkAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default)
        { TaskNodeLinks.Add((taskKey, workflowId, nodeKey)); return Task.CompletedTask; }

        public Task MirrorNodeAsync(string workflowId, NodeRecord node, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RemoveNodeMirrorAsync(string workflowId, string nodeKey, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RemoveTaskMirrorAsync(string taskKey, CancellationToken ct = default) => throw new NotImplementedException();
        public Task WriteRelationsAsync(string workflowId, string nodeKey, IReadOnlyCollection<NodeRelation> authoritative, CancellationToken ct = default) => throw new NotImplementedException();
        public Task ReconcileTaskAsync(string taskKey, IReadOnlyCollection<string> relatedNodeKeys, string workflowId = "", CancellationToken ct = default) => throw new NotImplementedException();
        public Task UnlinkTaskNodeAsync(string taskKey, string workflowId, string nodeKey, CancellationToken ct = default) => throw new NotImplementedException();
        public Task EnsureHistoryMirrorAsync(string historyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RemoveHistoryMirrorAsync(string historyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AttachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DetachHistoryTaskAsync(string historyId, string taskKey, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> GetHistoryTasksAsync(string historyId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AttachHistoryNodeAsync(string historyId, string workflowId, string nodeKey, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> GetHistoryNodeKeysAsync(string historyId, string workflowId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddSessionMessageLinkAsync(string sessionName, string workflowId, string nodeKey, CancellationToken ct = default) => throw new NotImplementedException();
        public Task AddRoundSequenceEdgeAsync(string workflowId, string fromNode, string toNode, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<string>> GetSessionNodeKeysAsync(string sessionName, string workflowId, CancellationToken ct = default) => throw new NotImplementedException();
        public Task WriteStaticReferencesAsync(string workflowId, IReadOnlyCollection<StaticReference> references, CancellationToken ct = default) => throw new NotImplementedException();
        public Task DeclareRelationsAsync(string workflowId, IReadOnlyCollection<RelationDeclarationEntry> declarations, CancellationToken ct = default) => throw new NotImplementedException();
        public Task UndeclareRelationsAsync(string workflowId, IReadOnlyCollection<string> relationIds, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<IReadOnlyList<RelationDeclarationEntry>> ListRelationsAsync(string? workflowId = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task<int> ReconcileAsync(MaintenanceScope scope, string? workflowId = null, string? nodeKey = null, CancellationToken ct = default) => throw new NotImplementedException();
        public Task RebuildAllAsync(CancellationToken ct = default) => throw new NotImplementedException();
    }
}

