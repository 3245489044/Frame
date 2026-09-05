using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Graph;
using Operation.DataStoreDriven.Abstractions;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using UniversalWorkflowFramework.Core.Models;
using TestInfrastructure;

namespace Agent.Persistence.Tests;

/// <summary>
/// ReferenceGraphCoordinator（唯一 LiteDB owner；只存 task/node 引用边，实体端点仅会话内存）单测：
/// CRUD + upsert、引用往返、边落盘→重载重建内存图（资源端点不持久）、RemoveResource 级联清边、图算法扩展。
/// </summary>
[TestClass]
public class GraphIndexedResourceReferenceStoreTests : IntegrationTestBase
{
    private static string _dir = string.Empty;
    private static string _dbPath = string.Empty;

    [TestInitialize]
    public void Setup()
    {
        _dir = CreateTempDirectory();
        _dbPath = Path.Combine(_dir, "graph.db");
    }

    private static ResourceIdentifier Res(string type, string id) => new() { ResourceType = type, Id = id };

    [TestMethod]
    public async Task ConcurrentWritesAndReads_NoGraphCorruption()
    {
        // B5 回归护栏：图读面（GetReferencesAsync/GetRelationsAsync）已与写面（AddReferenceAsync 的 _gate）互斥；
        // 并发写图时读侧不应读到不一致中间态/触发集合变更异常。多线程压测：写边 + 读邻接交替。
        await using var store = ReferenceGraphCoordinator.CreateAsync(_dbPath);
        var center = Res("node", "center");
        await store.AddResourceAsync(center, symbol: "center");

        const int writerCount = 4;
        const int readersPerWriter = 50;
        var errors = new System.Collections.Concurrent.ConcurrentBag<System.Exception>();

        // 并发写仅走 _gate 保护的写路径（AddReferenceAsync；端点经 EnsureNodeInMemory 在 gate 内注册），
        // 并发读走本次加锁的读面——精确复现 B5 竞态面（gated 写 vs 读枚举），不引入未加锁 AddResourceAsync 的字典竞态。
        var writerTasks = Enumerable.Range(0, writerCount).Select(async w =>
        {
            for (var i = 0; i < readersPerWriter; i++)
            {
                var leaf = Res("node", $"leaf-{w}-{i}");
                await store.AddReferenceAsync(center, leaf, "node_to_node", 1.0);
                // 并发读同一节点邻接（B5 原竞态面）。
                try { await store.GetReferencesAsync(center, includeIncoming: true); } catch (System.Exception ex) { errors.Add(ex); }
                try { await store.GetRelationsAsync("agent-round", "center", NodeRelationType.NodeToNode, includeIncoming: true); } catch (System.Exception ex) { errors.Add(ex); }
            }
        }).ToArray();

        await Task.WhenAll(writerTasks);
        Assert.AreEqual(0, errors.Count, $"并发读写下不应抛集合变更/不一致异常：{errors.FirstOrDefault()?.GetType().Name}");

        // 写入全部可见（一致性：最终收敛无丢失）。
        var final = await store.GetReferencesAsync(center, includeIncoming: true);
        Assert.AreEqual(writerCount * readersPerWriter, final.Count, "全部写边应被读到（读面串行化后无丢失）");
    }

    [TestMethod]
    public async Task Resource_And_Reference_Roundtrip()
    {
        await using var store = ReferenceGraphCoordinator.CreateAsync(_dbPath);

        var a = Res("module", "A");
        var b = Res("module", "B");

        await store.AddResourceAsync(a, new System.Collections.Generic.Dictionary<string, object> { ["v"] = "1" }, symbol: "symA");
        await store.AddResourceAsync(b, symbol: "symB");

        var gotA = await store.GetResourceAsync(a);
        Assert.IsNotNull(gotA);
        Assert.AreEqual("A", gotA!.Identifier.Id);
        Assert.AreEqual("module", gotA.Identifier.ResourceType);
        Assert.AreEqual("symA", gotA.Symbol);
        Assert.AreEqual("1", gotA.Metadata["v"].ToString());

        // upsert：重复 Add 覆盖既有（端点符号更新；实体以 SQL 为准）。
        await store.AddResourceAsync(a, symbol: "symA2");
        Assert.AreEqual("symA2", (await store.GetResourceAsync(a))!.Symbol);

        await store.AddReferenceAsync(a, b, "Dependency", strength: 0.9);

        var refs = await store.GetReferencesAsync(a, includeIncoming: false);
        Assert.AreEqual(1, refs.Count);
        Assert.AreEqual("A", refs[0].Source.Id);
        Assert.AreEqual("B", refs[0].Target.Id);
        Assert.AreEqual("module", refs[0].Target.ResourceType);
        Assert.AreEqual("Dependency", refs[0].RefType);
        Assert.AreEqual(0.9, refs[0].Strength, 1e-6);
        Assert.IsFalse(refs[0].IsIncoming);

        var inRefs = await store.GetReferencesAsync(b, includeIncoming: true);
        Assert.AreEqual(1, inRefs.Count);
        Assert.IsTrue(inRefs[0].IsIncoming);
        Assert.AreEqual("A", inRefs[0].Source.Id);
    }


    [TestMethod]
    public async Task Reload_From_LiteDB_Preserves_Edges()
    {
        // LiteDB 只持久边；实体端点（AddResource）为会话内存，重载后需由 SQL 写透重新注册。
        {
            await using var store = ReferenceGraphCoordinator.CreateAsync(_dbPath);
            var a = Res("module", "A");
            var b = Res("module", "B");
            await store.AddResourceAsync(a, symbol: "symA");
            await store.AddResourceAsync(b);
            await store.AddReferenceAsync(a, b, "Uses");
        } // dispose → 释放文件锁

        await using var reloaded = ReferenceGraphCoordinator.CreateAsync(_dbPath);
        // 端点符号已落盘 ur_endpoints（可重建缓存，权威在 SQL）：重载后还原 Symbol，避免造裸资源退化可读性。
        var restoredA = await reloaded.GetResourceAsync(Res("module", "A"));
        Assert.IsNotNull(restoredA);
        Assert.AreEqual("symA", restoredA!.Symbol);
        // 引用边持久：重载后边仍在（内存图由持久边重建）。
        var refs = await reloaded.GetReferencesAsync(Res("module", "A"), includeIncoming: false);
        Assert.AreEqual(1, refs.Count);
        Assert.AreEqual("Uses", refs[0].RefType);
        Assert.AreEqual("B", refs[0].Target.Id);
    }

    [TestMethod]
    public async Task Graph_Algorithms_On_Concrete_Type()
    {
        await using var store = ReferenceGraphCoordinator.CreateAsync(_dbPath);
        var a = Res("module", "A");
        var b = Res("module", "B");
        var c = Res("module", "C");
        await store.AddResourceAsync(a);
        await store.AddResourceAsync(b);
        await store.AddResourceAsync(c);
        await store.AddReferenceAsync(a, b, "Depends");
        await store.AddReferenceAsync(b, c, "Depends");

        var keyA = a.GetUniqueKey();
        var keyB = b.GetUniqueKey();
        var keyC = c.GetUniqueKey();

        CollectionAssert.Contains(store.GetNeighbors(keyA).ToList(), keyB);

        var paths = await store.FindPathsAsync(keyA, keyC, maxDepth: 5);
        Assert.AreEqual(1, paths.Count);
        CollectionAssert.AreEqual(new[] { keyA, keyB, keyC }, paths[0]);

        var deps = await store.GetDependenciesAsync(keyA);
        CollectionAssert.AreEqual(new[] { keyB, keyC }, deps.OrderBy(x => x).ToList());
    }

    [TestMethod]
    public async Task RemoveResource_Cascades_References_In_LiteDB()
    {
        await using var store = ReferenceGraphCoordinator.CreateAsync(_dbPath);
        var a = Res("module", "A");
        var b = Res("module", "B");
        await store.AddResourceAsync(a);
        await store.AddResourceAsync(b);
        await store.AddReferenceAsync(a, b, "Depends");

        await store.RemoveResourceAsync(a);

        // 内存图：a 及其引用已级联删除；LiteDB 边同步级联。
        Assert.IsNull(await store.GetResourceAsync(a));
        Assert.AreEqual(0, (await store.GetReferencesAsync(b)).Count);
    }

    [TestMethod]
    public async Task RemoveResource_Cascade_Persisted_Across_Reload()
    {
        {
            await using var store = ReferenceGraphCoordinator.CreateAsync(_dbPath);
            var a = Res("module", "A");
            var b = Res("module", "B");
            await store.AddResourceAsync(a);
            await store.AddResourceAsync(b);
            await store.AddReferenceAsync(a, b, "Depends");
            await store.RemoveResourceAsync(a);
        }

        await using var reloaded = ReferenceGraphCoordinator.CreateAsync(_dbPath);
        // durable 侧引用已随资源删除（级联），重载后 b 无入向引用。
        Assert.IsNull(await reloaded.GetResourceAsync(Res("module", "A")));
        Assert.AreEqual(0, (await reloaded.GetReferencesAsync(Res("module", "B"))).Count);
    }

    [TestMethod]
    public async Task AddReferenceAsync_Allows_Unregistered_Endpoint()
    {
        // coordinator 已移除端点 fail-fast：端点存在性由 SQL 保证（ur_tasks / ur_nodes），未注册端点仍可写边。
        await using var store = ReferenceGraphCoordinator.CreateAsync(_dbPath);
        var a = Res("module", "A");
        var b = Res("module", "B"); // b 未 AddResource（未注册端点）
        await store.AddResourceAsync(a);

        await store.AddReferenceAsync(a, b, "Dependency");

        var refs = await store.GetReferencesAsync(a, includeIncoming: false);
        Assert.AreEqual(1, refs.Count);
        Assert.AreEqual("Dependency", refs[0].RefType);
        Assert.AreEqual("B", refs[0].Target.Id);
    }
}
