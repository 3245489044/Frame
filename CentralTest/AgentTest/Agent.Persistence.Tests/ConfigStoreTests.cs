using System;
using System.IO;
using System.Threading.Tasks;
using Operation.DataStoreDriven.Abstractions;
using UniversalWorkflowFramework.StorageFramework.Abstractions;
using Operation.DataStoreDriven.Configuration;
using UniversalWorkflowFramework.StorageFramework.Configuration;
using Operation.DataStoreDriven.Core;
using UniversalWorkflowFramework.StorageFramework.Core;
using Operation.DataStoreDriven.Resolvers;
using Operation.DataStoreDriven.Sqlite;
using UniversalWorkflowFramework.StorageFramework.Sqlite;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using TestInfrastructure;

namespace Agent.Persistence.Tests;

[TestClass]
public class ConfigStoreTests : IntegrationTestBase
{
    private static string _dir = string.Empty;

    [TestInitialize]
    public void Setup() => _dir = CreateTempDirectory();

    [TestMethod]
    public async Task InMemoryConfigStore_SetGetAll()
    {
        var store = new InMemoryConfigStore();
        await store.InitializeAsync();
        Assert.IsNull(await store.GetAsync("k"));
        await store.SetAsync("k", "v");
        Assert.AreEqual("v", await store.GetAsync("k"));
        var all = await store.GetAllAsync();
        Assert.AreEqual(1, all.Count);
        Assert.AreEqual("v", all["k"]);
    }

    [TestMethod]
    public async Task NodeConfigStore_GetAllAsync_SkipsMalformedKeys_WithoutThrowing()
    {
        // B2 防护：脏 config 节点（Key 缺失且 NodeKey 为 null/过短/无前缀）不应越界切片或 NRE，应被安全跳过。
        var nodes = new InMemoryNodeStore();
        await nodes.PutAsync(new NodeRecord { NodeType = NodeTypes.Config, NodeKey = "config.ok", PayloadJson = "v1" }); // Key 缺失，NodeKey 正常 → 剥前缀得 "ok"
        await nodes.PutAsync(new NodeRecord { NodeType = NodeTypes.Config, NodeKey = string.Empty });                    // NodeKey 空 → 跳过
        await nodes.PutAsync(new NodeRecord { NodeType = NodeTypes.Config, NodeKey = "x" });                            // 无前缀/过短 → 跳过
        await nodes.PutAsync(new NodeRecord { NodeType = NodeTypes.Config, NodeKey = "config." });                      // 前缀后为空 → 跳过

        var store = new NodeConfigStore(nodes);
        var all = await store.GetAllAsync();
        Assert.AreEqual(1, all.Count, "仅格式合法的 config 节点被收录，脏数据跳过而非崩溃");
        Assert.AreEqual("v1", all["ok"]);
    }

    [TestMethod]
    public async Task SqliteConfigStore_RoundTripsThroughDataStore()
    {
        var options = new PersistenceOptions
        {
            Provider = PersistenceProvider.Sqlite,
            DatabasePath = Path.Combine(_dir, "config.db"),
            TablePrefix = "ur_",
            AutoCreateSchema = true,
        };
        await using var dataStore = (IDataStore)DataStoreProviderFactory.Create(options);
        await dataStore.InitializeAsync();

        await dataStore.Config.SetAsync("maxProgress", "7");
        Assert.AreEqual("7", await dataStore.Config.GetAsync("maxProgress"));
        Assert.AreEqual("7", (await dataStore.Config.GetAllAsync())["maxProgress"]);
    }

    [TestMethod]
    public async Task SqliteConfigStore_Write_Is_Tied_To_BackboneSqliteTransaction_SingleDbAtomic()
    {
        var options = new PersistenceOptions
        {
            Provider = PersistenceProvider.Sqlite,
            DatabasePath = Path.Combine(_dir, "config-tx.db"),
            TablePrefix = "ur_",
            AutoCreateSchema = true,
        };
        await using var dataStore = (IDataStore)DataStoreProviderFactory.Create(options);
        await dataStore.InitializeAsync();

        // 基线写入（自动提交）。
        await dataStore.Config.SetAsync("k0", "v0");
        Assert.AreEqual("v0", await dataStore.Config.GetAsync("k0"));

        // 三库收敛：config 为①基础节点库（SQLite ur_nodes）承载节点，与骨干同库同连接并纳入环境事务，
        // 故回滚一个骨干事务会连带回滚该事务内的 config 写（单库原子，跨介质隐患消除）。
        var store = (IPersistenceStore)SqlitePersistenceStore.Create(options);
        await store.InitializeAsync();
        try
        {
            await using (var tx = await store.BeginTransactionAsync())
            {
                await dataStore.Config.SetAsync("k1", "v1");
                await tx.RollbackAsync();
            }
            Assert.IsNull(await dataStore.Config.GetAsync("k1"), "config 写纳入骨干 SQLite 事务，回滚后应随之回滚（单库原子）");
            Assert.AreEqual("v0", await dataStore.Config.GetAsync("k0"), "事务外基线写不受影响");
        }
        finally
        {
            await store.DisposeAsync();
        }
    }
}
