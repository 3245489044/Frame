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
using UniversalWorkflowFramework.Core.Managers;
using TestInfrastructure;

namespace Agent.Persistence.Tests;

[TestClass]
public class DataStoreProviderFactoryTests : IntegrationTestBase
{
    private static string _dir = string.Empty;

    [TestInitialize]
    public void Setup() => _dir = CreateTempDirectory();

    [TestMethod]
    public void Factory_ResolvesSqliteAsFacadeWithNodeStoreBackedZones()
    {
        var store = DataStoreProviderFactory.Create(Options(PersistenceProvider.Sqlite));
        var facade = Assert.IsInstanceOfType<FacadeDataStore>(store);
        // 三库收敛：Sqlite 路径的①基础节点库为 SQLite ur_nodes，三区经节点库（NodeZoneStore）落为 NodeType 判别节点。
        Assert.IsInstanceOfType(facade.Zones, typeof(NodeZoneStore));
        Assert.IsInstanceOfType(facade.Nodes, typeof(SqliteNodeStore));
    }

    [TestMethod]
    public void Factory_ReusesSharedBackbone()
    {
        var options = Options(PersistenceProvider.Sqlite);
        var backbone = PersistenceProviderFactory.Create(options);
        var store = (FacadeDataStore)DataStoreProviderFactory.Create(options, sharedBackbone: backbone);
        // 单一写入口：数据驱动门面复用同一骨干（Snapshots 同实例），避免双骨干/双真相。
        Assert.AreSame(backbone.Snapshots, store.Snapshots);
        // 三库收敛：Sqlite 路径 checkpoint 经节点库（NodeType=checkpoint）承载，不再复用骨干的 SQL checkpoint 表。
        Assert.IsInstanceOfType(store.Checkpoints, typeof(NodeCheckpointStore));
    }

    [TestMethod]
    public void Factory_ResolvesLocalAsFacade()
    {
        var store = DataStoreProviderFactory.Create(Options(PersistenceProvider.Local),
            placeholderCheckpoints: new InMemoryCheckpointStore(),
            placeholderSnapshots: new InMemorySnapshotStore());
        Assert.IsInstanceOfType<FacadeDataStore>(store);
    }

    [TestMethod]
    public void Factory_ResolvesGraphAsFacade()
    {
        var store = DataStoreProviderFactory.Create(
            Options(PersistenceProvider.Graph),
            referenceManager: new DefaultUniversalReferenceManager(),
            placeholderCheckpoints: new InMemoryCheckpointStore(),
            placeholderSnapshots: new InMemorySnapshotStore());
        Assert.IsInstanceOfType<FacadeDataStore>(store);
    }

    [TestMethod]
    public void KeyMappingFactory_NodeBacked_IsConvergedPrimaryPath()
    {
        // D1 双后端收敛护栏：三库收敛后 KeyMapping 仅节点库承载（NodeKeyMappingStore，ur_nodes）；
        // 独立 ur_keymappings 路径（SqliteKeyMappingStore）已移除，无独立表回落。
        var nodes = new InMemoryNodeStore();
        var km = NodeStorageStoreFactory.CreateNodeKeyMappings(nodes);
        Assert.IsInstanceOfType<NodeKeyMappingStore>(km, "主路径应产出节点库承载（NodeKeyMappingStore）");

        // 写透落节点库：SaveAsync → ur_nodes 的 keymapping 节点（NodeType=keymapping），非独立表。
        km.SaveAsync(KeyMappingCategory.NodeInput, "wf.node", "{\"m\":1}").GetAwaiter().GetResult();
        var node = nodes.GetAsync(KeyMappingKeys.StoreNodeKey(KeyMappingCategory.NodeInput, "wf.node")).GetAwaiter().GetResult();
        Assert.IsNotNull(node, "KeyMapping 写入应落为 keymapping 节点（ur_nodes），而非独立 ur_keymappings 表");
        Assert.AreEqual(NodeTypes.KeyMapping, node!.NodeType);
    }

    [TestMethod]
    public void Factory_ThrowsOnUnknownProvider()
    {
        var options = Options((PersistenceProvider)99);
        var threw = false;
        try { DataStoreProviderFactory.Create(options); }
        catch (NotSupportedException) { threw = true; }
        Assert.IsTrue(threw);
    }

    [TestMethod]
    public async Task InMemoryZoneStore_AppendAndListRoundTrips()
    {
        var zones = new InMemoryZoneStore();
        await zones.InitializeAsync();

        var i1 = await zones.AppendInputAsync(new InputRecord { ThreadId = "t1", InputKey = "k1", PayloadJson = "{\"a\":1}" });
        var i2 = await zones.AppendInputAsync(new InputRecord { ThreadId = "t2", InputKey = "k2" });

        var all = await zones.ListInputsAsync();
        Assert.AreEqual(2, all.Count);
        Assert.AreEqual(i1, all[0].Id);
        Assert.AreEqual(i2, all[1].Id);

        var after = await zones.ListInputsAsync(afterId: i1);
        Assert.AreEqual(1, after.Count);
        Assert.AreEqual(i2, after[0].Id);

        var byThread = await zones.ListInputsAsync(threadId: "t1");
        Assert.AreEqual(1, byThread.Count);
        Assert.AreEqual("k1", byThread[0].InputKey);

        var o1 = await zones.AppendOutputAsync(new OutputRecord { ThreadId = "t1", InputKey = "k1", Status = "done" });
        var e1 = await zones.AppendEventAsync(new EventRecord { ThreadId = "t1", EventType = "run" });
        Assert.AreEqual(o1, (await zones.ListOutputsAsync())[0].Id);
        Assert.AreEqual(e1, (await zones.ListEventsAsync())[0].Id);
    }

    private static PersistenceOptions Options(PersistenceProvider provider) => new()
    {
        Provider = provider,
        // 每个测试独立文件路径：LiteDB/Local 后端构造即打开并持有文件句柄，跨测试共享路径会文件锁冲突。
        DatabasePath = Path.Combine(_dir, $"{provider}.db"),
        TablePrefix = "ur_",
        AutoCreateSchema = true,
    };
}
