using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;
using Operation.DataStoreDriven.Graph;
using TestInfrastructure;

namespace Agent.Persistence.Tests;

/// <summary>
/// 历史关系管理（Agent.Reference.HistoryRelationManager + ReferenceGrounding）测试：
/// History=Task 更上层的 class 容器，一个历史归拢多个任务；history→task / history→node / round_sequence 边写读、图查询、删除级联。
/// </summary>
[TestClass]
public class HistoryRelationManagerTests : IntegrationTestBase
{
    private const string Wf = "agent-round";
    private static string _dir = string.Empty;
    private static string _dbPath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _dir = CreateTempDirectory();
        _dbPath = Path.Combine(_dir, "graph.db");
    }

    [TestMethod]
    public async Task CreateHistory_BuildsRootEndpoint()
    {
        await using var graph = ReferenceGraphCoordinator.CreateAsync(_dbPath);
        var manager = new HistoryRelationManager(new ReferenceGrounding(graph), graph);

        var history = await manager.CreateAsync("hist-1", "项目一", System.Threading.CancellationToken.None);

        Assert.AreEqual("hist-1", history.HistoryId);
        // 图端点 ResourceType=history 存在（实体真源在 SQL，端点镜像承载图锚点）。
        var resource = await graph.GetResourceAsync(
            new UniversalWorkflowFramework.Core.Models.ResourceIdentifier
            {
                ResourceType = NodeResourceTypes.History,
                Id = "history.hist-1",
            });
        Assert.IsNotNull(resource, "创建历史应建 history.{historyId} 图根端点镜像");
    }

    [TestMethod]
    public async Task AttachTask_MultipleTasks_OneHistoryAggregates()
    {
        await using var graph = ReferenceGraphCoordinator.CreateAsync(_dbPath);
        var grounding = new ReferenceGrounding(graph);
        var manager = new HistoryRelationManager(grounding, graph);

        // 一个历史归拢两个任务（决策 4：会话管线自动挂；此处直接显式挂接验证多 task 归拢）。
        await manager.AttachTaskAsync("hist-A", "task-1");
        await manager.AttachTaskAsync("hist-A", "task-2");

        var tasks = await manager.GetTaskKeysAsync("hist-A");
        CollectionAssert.AreEquivalent(new[] { "task-1", "task-2" }, tasks.OrderBy(k => k).ToList(),
            "一个历史应归拢多个任务（history_owns_task 出边推导成员集）");

        var links = await manager.GetTasksAsync("hist-A");
        Assert.AreEqual(2, links.Count);
        Assert.IsTrue(links.All(l => l.HistoryId == "hist-A"));

        // 解除归拢：移除一个任务后成员集只余其一。
        await manager.DetachTaskAsync("hist-A", "task-1");
        var remaining = await manager.GetTaskKeysAsync("hist-A");
        CollectionAssert.AreEqual(new[] { "task-2" }, remaining.ToList(), "解除归拢应从历史成员集中移除该任务");
    }

    [TestMethod]
    public async Task AttachNode_And_Sequence_ProjectHistoryGraph()
    {
        await using var graph = ReferenceGraphCoordinator.CreateAsync(_dbPath);
        var grounding = new ReferenceGrounding(graph);
        var manager = new HistoryRelationManager(grounding, graph);

        // 历史归拢任务 + task→node M:N 关联（历史节点集**派生** = 其任务节点并集；不再直写 history→node 边）。
        await manager.AttachTaskAsync("hist-B", "task-b1");
        await manager.LinkNodeAsync("task-b1", Wf, "collect");
        await manager.AddSequenceAsync(Wf, "collect", "analyze");
        await manager.AddSequenceAsync(Wf, "analyze", "act");

        // 历史挂载节点（派生：经 task 归拢 + task→node cross_db_link 推导）应返回其工作流节点键集。
        var nodes = await manager.GetNodeKeysAsync("hist-B", Wf);
        CollectionAssert.AreEqual(new[] { "collect" }, nodes.ToList(),
            "历史节点集派生 = 其任务（task-b1）关联节点（collect）");

        // 时序边（round_sequence）已写：collect → analyze → act 链存在。
        var seq = await grounding.Graph.GetReferencesAsync(
            new UniversalWorkflowFramework.Core.Models.ResourceIdentifier
            {
                ResourceType = NodeResourceTypes.Node,
                Id = NodeRelationFactory.NodeId(Wf, "collect"),
            },
            includeIncoming: false);
        Assert.IsTrue(seq.Any(r => string.Equals(r.RefType, "round_sequence", StringComparison.Ordinal) && r.Target?.Id == NodeRelationFactory.NodeId(Wf, "analyze")),
            "时序边 round_sequence 应从 collect 指向 analyze（历史顺序由引用边决定）");
    }

    [TestMethod]
    public async Task UnlinkNode_MultiTaskManyToMany_OnlyDetachesThatTask()
    {
        await using var graph = ReferenceGraphCoordinator.CreateAsync(_dbPath);
        var grounding = new ReferenceGrounding(graph);
        var manager = new HistoryRelationManager(grounding, graph);

        // 真多对多：同一节点 collect 被两个任务共享引用。
        await manager.LinkNodeAsync("task-a", Wf, "collect");
        await manager.LinkNodeAsync("task-b", Wf, "collect");

        // 解除 task-a 的关联：仅移除 task-a→collect 边，不影响 task-b。
        await manager.UnlinkNodeAsync("task-a", Wf, "collect");
        var bNodes = await grounding.GetTaskNodeKeysAsync("task-b", Wf);
        CollectionAssert.AreEqual(new[] { "collect" }, bNodes.ToList(), "解除 task-a 关联不影响 task-b 的共享引用");
        var aNodes = await grounding.GetTaskNodeKeysAsync("task-a", Wf);
        Assert.AreEqual(0, aNodes.Count, "解除后 task-a 不再关联 collect");
    }

    [TestMethod]
    public async Task ListHistoryIds_EnumeratesHistoryRoots()
    {
        await using var graph = ReferenceGraphCoordinator.CreateAsync(_dbPath);
        var manager = new HistoryRelationManager(new ReferenceGrounding(graph), graph);

        await manager.CreateAsync("hist-x");
        await manager.CreateAsync("hist-y");

        var ids = await manager.ListHistoryIdsAsync();
        CollectionAssert.AreEquivalent(new[] { "hist-x", "hist-y" }, ids.OrderBy(i => i).ToList());
    }

    [TestMethod]
    public async Task DeleteHistory_RemovesRootCascade()
    {
        await using var graph = ReferenceGraphCoordinator.CreateAsync(_dbPath);
        var grounding = new ReferenceGrounding(graph);
        var manager = new HistoryRelationManager(grounding, graph);

        await manager.AttachTaskAsync("hist-del", "task-1");
        Assert.IsTrue(await manager.DeleteAsync("hist-del"), "删除历史应命中");

        // 级联清边：历史根端点被移除。
        var resource = await graph.GetResourceAsync(
            new UniversalWorkflowFramework.Core.Models.ResourceIdentifier
            {
                ResourceType = NodeResourceTypes.History,
                Id = "history.hist-del",
            });
        Assert.IsNull(resource, "删除历史应级联移除其图根端点镜像");
    }
}
