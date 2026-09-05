using System;
using System.Threading;
using System.Threading.Tasks;
using Agent.Contract.Nodes;
using Agent.Core.Nodes;
using Operation.HierarchyMethod.Nodes;
using Operation.HierarchyMethod.Nodes.Discovery;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;

namespace Agent.Persistence.Tests;

[TestClass]
public class NodeInjectionServiceTests
{
    private const string Wf = "agent-round";

    [TestMethod]
    public async Task InjectAsync_LoadsSelectedNodesPayloadsWithinBudget()
    {
        var store = new InMemoryNodeStore();
        await store.PutAsync(BizNode("n1", 1000)).ConfigureAwait(false);
        await store.PutAsync(BizNode("n2", 1000)).ConfigureAwait(false);

        var service = new NodeInjectionService(store, defaultBudgetTokens: 6000);
        var result = await service.InjectAsync(Wf, new[] { "n1", "n2" }, null, CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(2, result.Loaded.Count);
        Assert.AreEqual(0, result.Dropped.Count);
        Assert.IsTrue(result.UsedTokens <= 6000, "注入不得超出预算");
        Assert.IsTrue(result.Injected.Contains("n1") && result.Injected.Contains("n2"));
    }

    [TestMethod]
    public async Task InjectAsync_DropsNodesExceedingBudget()
    {
        var store = new InMemoryNodeStore();
        // 每个载荷约 20000 字符 ≈ 5000 tokens；预算 3000 → 仅 big1 可截断容纳，big2 被 dropped。
        await store.PutAsync(BizNode("big1", 20000)).ConfigureAwait(false);
        await store.PutAsync(BizNode("big2", 20000)).ConfigureAwait(false);

        var service = new NodeInjectionService(store, defaultBudgetTokens: 3000);
        var result = await service.InjectAsync(Wf, new[] { "big1", "big2" }, null, CancellationToken.None).ConfigureAwait(false);

        Assert.IsTrue(result.UsedTokens <= 3000, "注入不得超出预算");
        Assert.AreEqual(1, result.Dropped.Count, "超出预算的节点应被 dropped");
        Assert.IsTrue(result.Injected.Contains("[truncated]"), "超限载荷应截断注入而非丢弃");
    }

    [TestMethod]
    public async Task InjectAsync_MissingNodeIsDropped()
    {
        var store = new InMemoryNodeStore();
        var service = new NodeInjectionService(store, defaultBudgetTokens: 6000);

        var result = await service.InjectAsync(Wf, new[] { "ghost" }, null, CancellationToken.None).ConfigureAwait(false);

        Assert.AreEqual(1, result.Dropped.Count);
        Assert.AreEqual(0, result.Loaded.Count);
    }

    /// <summary>构造业务节点（输出并入业务节点形态）：NodeKey=裸 {node}，NodeType=node，PayloadJson=BusinessNodePayload。</summary>
    private static NodeRecord BizNode(string key, int textLength)
        => new()
        {
            NodeKey = key,
            NodeType = NodeTypes.Node,
            ThreadId = Wf,
            Status = "completed",
            PayloadJson = NodeOutputCodec.EncodeBusiness(new BusinessNodePayload
            {
                Symbol = key,
                Outputs = new NodeOutputDoc { Ports = { ["text"] = NodeOutputCodec.EncodeValue(new string('x', textLength)) } },
            }),
        };
}
