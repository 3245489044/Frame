using System.Linq;
using System.Threading;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.LoopFoldMethod.Reference.Gateways;

namespace Operation.Tests.LoopFoldMethod;

/// <summary>
/// Reference 编排门面（Gateways）测试 —— 只编排不写边、未装配守卫 no-op/空集诊断（LLM-free）。
/// 写路径行为经 HierarchyMethod.Tests.HistoryRelationManagerTests 通过转发覆盖；本类直接验证 Gateway 契约的空守卫语义。
/// </summary>
[TestClass]
public class GatewayTests
{
    [TestMethod]
    public void HistoryRelationGateway_Unassembled_ReturnsDiagnostics_NotThrows()
    {
        var gateway = new HistoryRelationGateway(maintenance: null, graph: null);
        var ct = CancellationToken.None;

        Assert.AreEqual("h-1", gateway.CreateAsync("h-1", ct).GetAwaiter().GetResult());
        Assert.IsFalse(gateway.DeleteAsync("h-1", ct).GetAwaiter().GetResult());
        Assert.AreEqual(0, gateway.GetTaskKeysAsync("h-1", ct).GetAwaiter().GetResult().Count);
        Assert.AreEqual(0, gateway.GetNodeKeysAsync("h-1", "wf", ct).GetAwaiter().GetResult().Count);
        Assert.AreEqual(0, gateway.ListHistoryIdsAsync(ct).GetAwaiter().GetResult().Count);

        // 写编排 no-op（不抛异常）。
        gateway.AttachTaskAsync("h-1", "t-1", ct).GetAwaiter().GetResult();
        gateway.DetachTaskAsync("h-1", "t-1", ct).GetAwaiter().GetResult();
        gateway.AttachNodeAsync("h-1", "wf", "n", ct).GetAwaiter().GetResult();
        gateway.AddSequenceAsync("wf", "n1", "n2", ct).GetAwaiter().GetResult();
    }

    [TestMethod]
    public void HistoryRelationGateway_EmptyHistoryId_Throws()
    {
        var gateway = new HistoryRelationGateway(maintenance: null, graph: null);
        Assert.ThrowsExactly<System.ArgumentException>(
            () => gateway.CreateAsync("", CancellationToken.None).GetAwaiter().GetResult());
    }

    [TestMethod]
    public void NodeRelationGateway_Unassembled_NodeWriteNoOp_ResourceReadEmpty()
    {
        var gateway = new NodeRelationGateway(grounding: null, store: null);
        var ct = CancellationToken.None;

        // 节点执行期写：未装配守卫 → no-op（不抛）。
        gateway.WriteRelationsAsync("wf", "n", System.Array.Empty<Operation.DataStoreDriven.Abstractions.NodeRelation>(), isWriteThroughStore: false, ct).GetAwaiter().GetResult();
        gateway.EnsureTaskNodeLinkAsync("t-1", "wf", "n", ct).GetAwaiter().GetResult();

        // 资源读：未装配 store → 空集/null。
        Assert.IsNull(gateway.GetResourceAsync("node", "n", ct).GetAwaiter().GetResult());
        Assert.AreEqual(0, gateway.ListResourcesAsync(ct).GetAwaiter().GetResult().Count);
        Assert.AreEqual(0, gateway.ListReferencesAsync("node", "n", ct: ct).GetAwaiter().GetResult().Count);
    }

    [TestMethod]
    public void NodeRelationGateway_Unassembled_ResourceWrite_Throws()
    {
        var gateway = new NodeRelationGateway(grounding: null, store: null);
        Assert.ThrowsExactly<System.InvalidOperationException>(
            () => gateway.AddResourceAsync("node", "n").GetAwaiter().GetResult());
        Assert.ThrowsExactly<System.InvalidOperationException>(
            () => gateway.AddReferenceAsync("task", "t", "node", "n", Operation.LoopFoldMethod.Reference.Model.ReferenceTypes.CrossDbLink).GetAwaiter().GetResult());
    }

    [TestMethod]
    public void NodeRelationGateway_EmptyTaskKey_NodeLinkNoOp()
    {
        // 有守卫但 taskKey 空（无会话上下文，如直跑/单测）→ no-op，不抛。
        var gateway = new NodeRelationGateway(grounding: null, store: null);
        gateway.EnsureTaskNodeLinkAsync(null, "wf", "n", CancellationToken.None).GetAwaiter().GetResult();
        gateway.EnsureTaskNodeLinkAsync("", "wf", "n", CancellationToken.None).GetAwaiter().GetResult();
    }
}
