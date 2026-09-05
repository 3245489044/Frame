using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Agent.Contract.Nodes;
using Agent.Core.Nodes;
using Operation.HierarchyMethod.Nodes.Discovery;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;
using Operation.DataStoreDriven.Graph;
using TestInfrastructure;

namespace Agent.Persistence.Tests;

[TestClass]
public class NodeDiscoveryProjectionTests : IntegrationTestBase
{
    private const string Wf = "agent-round";
    private static string _dir = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _dir = CreateTempDirectory();
    }

    [TestMethod]
    public async Task ProjectTaskNodes_ReturnsOnlyTaskScopedCandidatesWithSummary()
    {
        await using var graph = ReferenceGraphCoordinator.CreateAsync(Path.Combine(_dir, "graph.db"));
        var grounding = new ReferenceGrounding(graph);
        var store = new InMemoryNodeStore();

        await WriteNodeAsync(store, grounding, "task-A", "n1", "result=hello");
        await WriteNodeAsync(store, grounding, "task-A", "n2", "result=world");
        await WriteNodeAsync(store, grounding, "task-B", "other", "result=other-task");

        var service = new NodeProjectionService(grounding, store);
        var result = await service.ProjectAsync("task-A", Wf, 0, CancellationToken.None);

        Assert.AreEqual(2, result.Candidates.Count, "只应投影 task-A 挂载节点，排除跨 task 污染");
        var keys = result.Candidates.Select(c => c.NodeKey).OrderBy(x => x).ToList();
        CollectionAssert.AreEqual(new[] { "n1", "n2" }, keys);
        Assert.IsTrue(result.Candidates.All(c => !string.IsNullOrWhiteSpace(c.Summary)), "候选应携带一句话摘要");
        Assert.IsTrue(result.TotalEstTokens > 0, "候选应携带累计 token 估算");
    }

    [TestMethod]
    public async Task ProjectTaskNodes_LimitTruncatesCandidateList()
    {
        await using var graph = ReferenceGraphCoordinator.CreateAsync(Path.Combine(_dir, "graph.db"));
        var grounding = new ReferenceGrounding(graph);
        var store = new InMemoryNodeStore();
        await WriteNodeAsync(store, grounding, "task-A", "n1", "a");
        await WriteNodeAsync(store, grounding, "task-A", "n2", "b");
        await WriteNodeAsync(store, grounding, "task-A", "n3", "c");

        var service = new NodeProjectionService(grounding, store);
        var result = await service.ProjectAsync("task-A", Wf, 2, CancellationToken.None);

        Assert.AreEqual(2, result.Candidates.Count);
    }

    [TestMethod]
    public async Task ProjectTaskNodes_UnknownTaskReturnsEmpty()
    {
        await using var graph = ReferenceGraphCoordinator.CreateAsync(Path.Combine(_dir, "graph.db"));
        var grounding = new ReferenceGrounding(graph);
        var service = new NodeProjectionService(grounding, new InMemoryNodeStore());

        var result = await service.ProjectAsync("task-unknown", Wf, 0, CancellationToken.None);

        Assert.AreEqual(0, result.Candidates.Count);
    }

    /// <summary>写业务节点 + 输出记录（摘要来源）+ task→node cross_db_link 边。</summary>
    private static async Task WriteNodeAsync(InMemoryNodeStore store, ReferenceGrounding grounding, string taskKey, string nodeKey, string portValue)
    {
        await store.PutAsync(new NodeRecord
        {
            NodeKey = nodeKey,
            NodeType = NodeTypes.Node,
            ThreadId = Wf,
            Status = "executed",
            PayloadJson = $"{{\"Symbol\":\"{nodeKey}\"}}",
        }).ConfigureAwait(false);

        var doc = new NodeOutputDoc();
        doc.Ports[nodeKey] = NodeOutputCodec.EncodeValue(portValue);
        await store.PutAsync(new NodeRecord
        {
            NodeKey = NodeOutputKeys.NodeKey(Wf, nodeKey),
            NodeType = NodeTypes.Output,
            ThreadId = Wf,
            Status = "completed",
            PayloadJson = NodeOutputCodec.EncodeDoc(doc),
        }).ConfigureAwait(false);

        await grounding.EnsureTaskNodeLinkAsync(taskKey, Wf, nodeKey, CancellationToken.None).ConfigureAwait(false);
    }
}
