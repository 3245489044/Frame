using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.DataStoreDriven.Abstractions;
using Operation.DataStoreDriven.Configuration;
using Operation.DataStoreDriven.Core;
using Operation.DataStoreDriven.Sqlite;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Configuration;
using UniversalWorkflowFramework.StorageFramework.Core;
using UniversalWorkflowFramework.StorageFramework.Sqlite;
using TestInfrastructure;

namespace Agent.Persistence.Tests;

/// <summary>
/// SQLite 基础节点库（ur_nodes 极简键值+JSON）回归保护（clinerules TH-12/TH-13）。
/// PutAsync 契约 = "NodeKey 幂等 upsert"：同一 NodeKey 重复写入应覆写既有行、不产生重复、不抛唯一索引冲突。
/// ListAsync 经 JSON 派生生成列按 NodeType/ThreadId 过滤 + afterId 分页。
/// </summary>
[TestClass]
public class SqliteNodeStoreTests : IntegrationTestBase
{
    private string _dir = string.Empty;
    private SqliteNodeStore _nodes = null!;
    private SqliteSession _session = null!;

    [TestInitialize]
    public void Setup()
    {
        _dir = CreateTempDirectory();
        var dbPath = Path.Combine(_dir, "nodes.db");
        var options = new PersistenceOptions
        {
            Provider = PersistenceProvider.Sqlite,
            DatabasePath = dbPath,
            TablePrefix = "ur_",
            AutoCreateSchema = true,
        };
        _session = new SqliteSession(new SqliteConnectionFactory(options).ConnectionString);
        _nodes = new SqliteNodeStore(_session, "ur_nodes");
        _nodes.InitializeAsync().GetAwaiter().GetResult();
    }

    [TestMethod]
    public async Task PutAsync_Is_Idempotent_By_NodeKey_Overwrite_Not_Throw()
    {
        // 同一 NodeKey（Id 未携带）重复写入：应返回同一 Id、payload 被覆写、不抛唯一冲突。
        var id1 = await _nodes.PutAsync(new NodeRecord
        {
            NodeKey = "km.in.x",
            NodeType = NodeTypes.KeyMapping,
            Key = "in.x",
            PayloadJson = "{\"v\":1}",
        });
        var first = await _nodes.GetAsync("km.in.x");
        Assert.IsNotNull(first!.CreatedAt, "CreatedAt 应随 Json 持久化");
        var firstCreatedAt = first!.CreatedAt;

        var id2 = await _nodes.PutAsync(new NodeRecord
        {
            NodeKey = "km.in.x",
            NodeType = NodeTypes.KeyMapping,
            Key = "in.x",
            PayloadJson = "{\"v\":2}",
        });

        Assert.AreEqual(id1, id2, "同一 NodeKey 幂等覆写应返回同一 Id，不应产生重复行");

        var got = await _nodes.GetAsync("km.in.x");
        Assert.IsNotNull(got);
        Assert.AreEqual("{\"v\":2}", got!.PayloadJson, "既有节点的载荷应被覆写");
        Assert.AreEqual(NodeTypes.KeyMapping, got.NodeType, "NodeType 应从 JSON 派生还原");
        Assert.AreEqual("in.x", got.Key, "Key 应从 JSON 派生还原");
        Assert.AreEqual(firstCreatedAt, got.CreatedAt, "幂等覆写应保留首次 CreatedAt（随 Json）");
        Assert.IsNotNull(got.UpdatedAt, "UpdatedAt 应随 Json 持久化");
        Assert.AreNotEqual(0L, id1, "应返回自增 Id");
    }

    [TestMethod]
    public async Task NodeKeyMappingStore_SaveTwice_Is_Idempotent()
    {
        var km = new NodeKeyMappingStore(_nodes);
        await km.SaveAsync(KeyMappingCategory.NodeInput, "wf.n1", "{\"a\":1}");
        await km.SaveAsync(KeyMappingCategory.NodeInput, "wf.n1", "{\"a\":2}");

        var got = await _nodes.GetAsync("keymapping.NodeInput.wf.n1");
        Assert.IsNotNull(got);
        Assert.AreEqual("{\"a\":2}", got!.PayloadJson, "同一 keymapping 重复写入应覆写载荷而非新增重复行");
    }

    [TestMethod]
    public async Task ListAsync_Filters_By_NodeType_ThreadId_And_Pages_By_Id()
    {
        await _nodes.PutAsync(new NodeRecord { NodeKey = "input:t1:a", NodeType = NodeTypes.Input, ThreadId = "t1", PayloadJson = "{\"a\":1}" });
        await _nodes.PutAsync(new NodeRecord { NodeKey = "input:t2:b", NodeType = NodeTypes.Input, ThreadId = "t2", PayloadJson = "{\"a\":2}" });
        await _nodes.PutAsync(new NodeRecord { NodeKey = "output:t1:c", NodeType = NodeTypes.Output, ThreadId = "t1", PayloadJson = "{\"a\":3}" });

        var byType = await _nodes.ListAsync(nodeType: NodeTypes.Input);
        Assert.AreEqual(2, byType.Count, "按 NodeType=input 应命中 2 条");

        var byThread = await _nodes.ListAsync(nodeType: NodeTypes.Input, threadId: "t1");
        Assert.AreEqual(1, byThread.Count, "按 NodeType+ThreadId 应命中 1 条");
        Assert.AreEqual("t1", byThread[0].ThreadId);

        var byThreadOnly = await _nodes.ListAsync(threadId: "t1");
        Assert.AreEqual(2, byThreadOnly.Count, "仅按 ThreadId 应命中 2 条");

        // afterId 分页：仅返回比 afterId 大的记录
        var first = (await _nodes.ListAsync(nodeType: NodeTypes.Input, limit: 1))[0];
        var page = await _nodes.ListAsync(nodeType: NodeTypes.Input, afterId: first.Id);
        Assert.AreEqual(1, page.Count, "afterId 后应只剩 1 条");
        Assert.IsTrue(page[0].Id > first.Id);
    }

    [TestMethod]
    public async Task Delete_And_DeleteMany_Are_Idempotent()
    {
        await _nodes.PutAsync(new NodeRecord { NodeKey = "k1", PayloadJson = "{}" });
        await _nodes.PutAsync(new NodeRecord { NodeKey = "k2", PayloadJson = "{}" });

        Assert.IsTrue(await _nodes.DeleteAsync("k1"));
        Assert.IsFalse(await _nodes.DeleteAsync("k1"), "重复删除同键应返回 false（幂等）");
        Assert.IsNull(await _nodes.GetAsync("k1"));

        var removed = await _nodes.DeleteManyAsync(new[] { "k2", "k2", "k3" });
        Assert.AreEqual(1, removed.Count, "批量删除应去重并只返回实际删除的键");
        Assert.AreEqual("k2", removed[0]);
    }

    [TestMethod]
    public async Task NodeZoneStore_RoundTrips_Through_SqliteNodeStore()
    {
        var zones = new NodeZoneStore(_nodes);
        var i1 = await zones.AppendInputAsync(new InputRecord { ThreadId = "t1", InputKey = "k1", PayloadJson = "{\"a\":1}" });
        var i2 = await zones.AppendInputAsync(new InputRecord { ThreadId = "t2", InputKey = "k2" });

        var all = await zones.ListInputsAsync();
        Assert.AreEqual(2, all.Count);
        Assert.AreEqual(i1, all[0].Id);
        Assert.AreEqual("k1", all[0].InputKey);

        var byThread = await zones.ListInputsAsync(threadId: "t1");
        Assert.AreEqual(1, byThread.Count);
        Assert.AreEqual("{\"a\":1}", byThread[0].PayloadJson);
    }

    [TestMethod]
    public void Schema_Has_Only_Id_NodeKey_Json_Columns()
    {
        // 方案 A：ur_nodes 收敛为纯「键值 + 一个 JSON」——CreatedAt/UpdatedAt/NodeType/ThreadId 全部随 Json，不建物理列。
        var cols = _session.ExecuteQuery("PRAGMA table_info(ur_nodes)");
        var names = cols
            .Select(c => (c["name"] as string) ?? string.Empty)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(n => n.ToLowerInvariant())
            .ToList();
        CollectionAssert.AreEqual(
            new[] { "id", "json", "nodekey" },
            names,
            "ur_nodes 应仅剩 Id/NodeKey/Json 三列，其余元数据随 Json 持久化");
    }

    [TestMethod]
    public async Task LegacySchema_Migrates_Into_Json_And_Drops_Columns()
    {
        // 旧版 7 列表（含 CreatedAt/UpdatedAt 物理列 + NodeType/ThreadId 生成列）自动迁移为纯「键值 + 一个 JSON」。
        var dbPath = Path.Combine(_dir, "legacy.db");
        var options = new PersistenceOptions
        {
            Provider = PersistenceProvider.Sqlite,
            DatabasePath = dbPath,
            TablePrefix = "ur_",
            AutoCreateSchema = true,
        };
        var session = new SqliteSession(new SqliteConnectionFactory(options).ConnectionString);
        session.ExecuteNonQuery(
            "CREATE TABLE IF NOT EXISTS ur_nodes (" +
            "[Id] INTEGER PRIMARY KEY AUTOINCREMENT, [NodeKey] TEXT NOT NULL UNIQUE, [Json] TEXT NOT NULL, " +
            "[CreatedAt] TEXT NULL, [UpdatedAt] TEXT NULL, " +
            "[NodeType] TEXT GENERATED ALWAYS AS (json_extract([Json],'$.NodeType')) STORED, " +
            "[ThreadId] TEXT GENERATED ALWAYS AS (json_extract([Json],'$.ThreadId')) STORED);");
        session.ExecuteNonQuery(
            "INSERT INTO ur_nodes ([NodeKey],[Json],[CreatedAt],[UpdatedAt]) VALUES (@k,@j,@c,@u)",
            PersistenceHelper.CreateParameters(
                ("k", "km.in.x"),
                ("j", "{\"NodeType\":\"keymapping\",\"ThreadId\":\"t1\",\"Key\":\"in.x\",\"PayloadJson\":\"{}\"}"),
                ("c", "2026-01-01T00:00:00Z"),
                ("u", "2026-01-02T00:00:00Z")));

        var store = new SqliteNodeStore(session, "ur_nodes");
        await store.InitializeAsync();

        var got = await store.GetAsync("km.in.x");
        Assert.IsNotNull(got);
        Assert.AreEqual("2026-01-01T00:00:00Z", got!.CreatedAt, "旧列 CreatedAt 应折入 Json");
        Assert.AreEqual("2026-01-02T00:00:00Z", got.UpdatedAt, "旧列 UpdatedAt 应折入 Json");
        Assert.AreEqual(NodeTypes.KeyMapping, got.NodeType, "NodeType 应保持 Json 派生还原");

        var cols = session.ExecuteQuery("PRAGMA table_info(ur_nodes)");
        var names = cols
            .Select(c => (c["name"] as string) ?? string.Empty)
            .OrderBy(n => n, StringComparer.OrdinalIgnoreCase)
            .Select(n => n.ToLowerInvariant())
            .ToList();
        CollectionAssert.AreEqual(new[] { "id", "json", "nodekey" }, names, "旧列应被清理，仅剩 Id/NodeKey/Json");
    }
}
