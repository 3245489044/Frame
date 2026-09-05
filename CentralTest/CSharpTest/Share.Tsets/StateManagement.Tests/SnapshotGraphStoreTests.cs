using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Core;

namespace Share.StateManagement.Tests;

/// <summary>
/// 「三种数据」统一模型的存储层测试：标准化节点（INodeStore，经 <see cref="InMemoryNodeStore"/> 承载）+ 快照图引用逻辑。
/// 覆盖标准节点查询（含 NodeType/ThreadId 过滤、幂等 upsert、批量删除），以及
/// ①基于任务快照的引用查询（GetGraphAsync）、②基于任务快照的引用删除（DeleteGraphAsync，级联清理）。
/// 注：SQLite ur_resources 已下线（引用关系真源为 LiteDB 图库），快照图回落 <see cref="InMemorySnapshotGraphStore"/>，
/// 节点载荷以内存承载；快照↔节点关系由节点表 ur_nodes（节点自带 ThreadId）承载，不再建独立 SQL 边表。
/// </summary>
[TestClass]
public class SnapshotGraphStoreTests
{
    private static InMemoryNodeStore OpenNodes() => new();

    private static ISnapshotGraphStore OpenGraph(InMemoryNodeStore nodes)
        => new InMemorySnapshotGraphStore(nodes);

    // ---------- ① 标准化节点数据（INodeStore 端口契约） ----------

    [TestMethod]
    public async Task NodeStore_Put_Get_Roundtrip()
    {
        var store = OpenNodes();
        await store.InitializeAsync();
        await store.PutAsync(new NodeRecord { NodeKey = "n1", NodeType = NodeTypes.Output, ThreadId = "t1", PayloadJson = "{\"ok\":true}" });

        var got = await store.GetAsync("n1");
        Assert.IsNotNull(got);
        Assert.AreEqual("n1", got!.NodeKey);
        Assert.AreEqual(NodeTypes.Output, got.NodeType);
        Assert.AreEqual("t1", got.ThreadId);
    }

    [TestMethod]
    public async Task NodeStore_Upsert_ByIdempotentKey_DoesNotDuplicate()
    {
        var store = OpenNodes();
        await store.InitializeAsync();
        await store.PutAsync(new NodeRecord { NodeKey = "n1", NodeType = NodeTypes.Input, PayloadJson = "{\"v\":1}" });
        await store.PutAsync(new NodeRecord { NodeKey = "n1", NodeType = NodeTypes.Input, PayloadJson = "{\"v\":2}" }); // 幂等覆写

        var all = await store.ListAsync();
        Assert.AreEqual(1, all.Count);
        Assert.AreEqual("{\"v\":2}", all[0].PayloadJson);
    }

    [TestMethod]
    public async Task NodeStore_List_FiltersByType_AndThread()
    {
        var store = OpenNodes();
        await store.InitializeAsync();
        await store.PutAsync(new NodeRecord { NodeKey = "a", NodeType = NodeTypes.Input, ThreadId = "t1" });
        await store.PutAsync(new NodeRecord { NodeKey = "b", NodeType = NodeTypes.Output, ThreadId = "t1" });
        await store.PutAsync(new NodeRecord { NodeKey = "c", NodeType = NodeTypes.Input, ThreadId = "t2" });

        Assert.AreEqual(2, (await store.ListAsync(NodeTypes.Input)).Count);
        Assert.AreEqual(2, (await store.ListAsync(threadId: "t1")).Count);
        Assert.AreEqual(1, (await store.ListAsync(NodeTypes.Input, "t1")).Count);
    }

    [TestMethod]
    public async Task NodeStore_DeleteMany_Returns_RemovedKeys()
    {
        var store = OpenNodes();
        await store.InitializeAsync();
        await store.PutAsync(new NodeRecord { NodeKey = "a", NodeType = NodeTypes.Input });
        await store.PutAsync(new NodeRecord { NodeKey = "b", NodeType = NodeTypes.Input });

        var removed = await store.DeleteManyAsync(new[] { "a", "b", "missing" });
        CollectionAssert.AreEquivalent(new[] { "a", "b" }, removed.ToArray());
        Assert.AreEqual(0, (await store.ListAsync()).Count);
    }

    // ---------- ② 状态根（ur_tasks） + ③ 引用逻辑（ur_resources） ----------

    [TestMethod]
    public async Task Graph_SaveSnapshot_LoadSnapshot_Roundtrip()
    {
        var graph = OpenGraph(OpenNodes());
        await graph.InitializeAsync();
        await graph.SaveSnapshotAsync("t1", "s1", "{\"round\":1}");

        Assert.AreEqual("{\"round\":1}", await graph.LoadSnapshotAsync("t1", "s1"));
        Assert.IsNull(await graph.LoadSnapshotAsync("t1", "s2"));
    }

    [TestMethod]
    public async Task Graph_Attach_GetGraph_ReferenceQuery_ReturnsRootAndNodes()
    {
        var nodes = OpenNodes();
        await nodes.InitializeAsync();
        await nodes.PutAsync(new NodeRecord { NodeKey = "in_1", NodeType = NodeTypes.Input, ThreadId = "t1" });
        await nodes.PutAsync(new NodeRecord { NodeKey = "out_1", NodeType = NodeTypes.Output, ThreadId = "t1" });
        await nodes.PutAsync(new NodeRecord { NodeKey = "other_1", NodeType = NodeTypes.Input, ThreadId = "t2" });

        var graph = OpenGraph(nodes);
        await graph.InitializeAsync();
        await graph.SaveSnapshotAsync("t1", "s1", "{\"round\":1}");
        await graph.AttachNodeAsync("t1", "s1", "in_1");
        await graph.AttachNodeAsync("t1", "s1", "out_1");
        await graph.AttachNodeAsync("t1", "s1", "in_1"); // 幂等，不重复

        // 基于任务快照的引用查询：取回根 + 全部引用节点；跨类型的 input/output 一并返回，未挂载节点不返回
        var result = await graph.GetGraphAsync("t1", "s1");
        Assert.AreEqual("{\"round\":1}", result.SnapshotJson);
        CollectionAssert.AreEquivalent(new[] { "in_1", "out_1" }, result.Nodes.Select(n => n.NodeKey).ToArray());
    }

    [TestMethod]
    public async Task Graph_DeleteGraph_ReferenceDelete_Cascades_RootAndNodes()
    {
        var nodes = OpenNodes();
        await nodes.InitializeAsync();
        await nodes.PutAsync(new NodeRecord { NodeKey = "in_1", NodeType = NodeTypes.Input, ThreadId = "t1" });
        await nodes.PutAsync(new NodeRecord { NodeKey = "in_2", NodeType = NodeTypes.Input, ThreadId = "t1" });

        var graph = OpenGraph(nodes);
        await graph.InitializeAsync();
        await graph.SaveSnapshotAsync("t1", "s1", "{\"round\":1}");
        await graph.AttachNodeAsync("t1", "s1", "in_1");
        await graph.AttachNodeAsync("t1", "s1", "in_2");

        // 基于任务快照的引用删除：删根 + 引用边 + 全部关联节点
        var removed = await graph.DeleteGraphAsync("t1", "s1");
        CollectionAssert.AreEquivalent(new[] { "in_1", "in_2" }, removed.ToArray());

        Assert.IsNull(await graph.LoadSnapshotAsync("t1", "s1"), "快照根应被删除");
        Assert.IsNull(await nodes.GetAsync("in_1"), "被引用节点应被级联删除");
        Assert.IsNull(await nodes.GetAsync("in_2"));
        Assert.AreEqual(0, (await graph.GetGraphAsync("t1", "s1")).Nodes.Count);
    }

    [TestMethod]
    public async Task Graph_DeleteGraph_SharedNode_SkippedWhenStillReferenced()
    {
        var nodes = OpenNodes();
        await nodes.InitializeAsync();
        await nodes.PutAsync(new NodeRecord { NodeKey = "shared_1", NodeType = NodeTypes.Input, ThreadId = "t1" });
        await nodes.PutAsync(new NodeRecord { NodeKey = "private_1", NodeType = NodeTypes.Input, ThreadId = "t1" });

        var graph = OpenGraph(nodes);
        await graph.InitializeAsync();
        // shared_1 同时挂载到 s1 与 s2；private_1 仅挂载到 s1
        await graph.SaveSnapshotAsync("t1", "s1", "{\"round\":1}");
        await graph.AttachNodeAsync("t1", "s1", "shared_1");
        await graph.AttachNodeAsync("t1", "s1", "private_1");
        await graph.SaveSnapshotAsync("t1", "s2", "{\"round\":2}");
        await graph.AttachNodeAsync("t1", "s2", "shared_1");

        // 删除 s1：返回其全部挂载键，但共享节点因仍被 s2 引用而跳过删除
        var removed = await graph.DeleteGraphAsync("t1", "s1");
        CollectionAssert.AreEquivalent(new[] { "shared_1", "private_1" }, removed.ToArray());

        Assert.IsNull(await graph.LoadSnapshotAsync("t1", "s1"), "s1 快照根应被删除");
        Assert.IsNull(await nodes.GetAsync("private_1"), "仅 s1 引用的节点应被级联删除");
        Assert.IsNotNull(await nodes.GetAsync("shared_1"), "仍被 s2 引用的共享节点不应删除");
        CollectionAssert.AreEquivalent(new[] { "shared_1" },
            (await graph.GetGraphAsync("t1", "s2")).Nodes.Select(n => n.NodeKey).ToArray(), "s2 图应保留共享节点");
    }
}
