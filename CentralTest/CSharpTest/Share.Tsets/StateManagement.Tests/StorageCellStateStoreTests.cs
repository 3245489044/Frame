using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using Share.StateManagement;
using StorageLayer = TConfig.StorageLayer;

namespace Share.StateManagement.Tests;

[TestClass]
public class StorageCellStateStoreTests
{
    [TestMethod]
    public void StorageCellStateStore_RoundTrips_Strings()
    {
        var engine = new StorageEngine("test-engine");
        var store = new StorageCellStateStore(engine.RootCell);

        Assert.IsTrue(store.IsEmpty);

        store.Set("a", "1");
        store.Set("b", "2");

        Assert.AreEqual("1", store.Get("a"));
        Assert.AreEqual(2, store.Count);
        Assert.IsTrue(store.Has("a"));
        Assert.IsFalse(store.Has("missing"));

        store.Remove("a");
        Assert.IsNull(store.Get("a"));
        Assert.AreEqual(1, store.Count);
        Assert.IsFalse(store.IsEmpty);
    }

    [TestMethod]
    public void StorageCellStateStore_RemoveByPrefix_And_GetAll()
    {
        var engine = new StorageEngine("test-engine");
        var store = new StorageCellStateStore(engine.RootCell);

        store.SetRange(new[]
        {
            new KeyValuePair<string, string?>("k1", "v1"),
            new KeyValuePair<string, string?>("k2", "v2"),
            new KeyValuePair<string, string?>("x1", "v3"),
        });

        store.RemoveByPrefix("k");
        Assert.AreEqual(1, store.Count);
        CollectionAssert.AreEqual(new[] { "x1" }, store.GetAllKeys().ToArray());

        var all = store.GetAll();
        Assert.AreEqual("v3", all["x1"]);
    }

    [TestMethod]
    public void StorageCellStateStore_GetOrAdd_Adds_Then_Gets()
    {
        var engine = new StorageEngine("test-engine");
        var store = new StorageCellStateStore(engine.RootCell);

        Assert.AreEqual("d", store.GetOrAdd("k", "d"));
        Assert.AreEqual("d", store.GetOrAdd("k", "ignored"));
        Assert.AreEqual(1, store.Count);
    }

    [TestMethod]
    public void StorageCellStateStore_Clear_Empties_Cell()
    {
        var engine = new StorageEngine("test-engine");
        var store = new StorageCellStateStore(engine.RootCell);

        store.Set("a", "1");
        store.Set("b", "2");

        store.Clear();
        Assert.AreEqual(0, store.Count);
        Assert.IsTrue(store.IsEmpty);
    }
}

[TestClass]
public class StateManagerEngineTests
{
    [TestMethod]
    public void StateManager_WithEngine_GlobalStore_IsBackedByRootCell()
    {
        var engine = new StorageEngine("test-engine");
        var manager = new StateManager(storageEngine: engine);

        manager.GlobalStore.Set("g", "1");
        Assert.AreEqual("1", manager.GlobalStore.Get("g"));

        // 全局存储写到底层引擎的根会话。
        Assert.IsTrue(engine.RootCell.Contains("g", StorageLayer.Session));
    }

    [TestMethod]
    public void StateManager_WithEngine_Scope_Creates_EngineSession()
    {
        var engine = new StorageEngine("test-engine");
        var manager = new StateManager(storageEngine: engine);

        var scope = manager.CreateScope("s1");
        scope.Set("k", "v");
        Assert.AreEqual("v", scope.Get("k"));

        Assert.AreEqual("s1", manager.GetScopeNames().Single());
        Assert.IsNotNull(manager.GetScope("s1"));
        Assert.IsNotNull(engine.GetSession("s1"));

        // scope 数据落在引擎会话 cell 上。
        Assert.IsTrue(engine.GetSession("s1")!.Contains("k", StorageLayer.Session));

        Assert.IsTrue(manager.ReleaseScope("s1"));
        Assert.IsNull(manager.GetScope("s1"));
        Assert.IsNull(engine.GetSession("s1"));
        Assert.AreEqual(0, manager.GetScopeNames().Count());
    }

    [TestMethod]
    public void StateManager_WithEngine_DuplicateScope_Throws_WithoutSessionLeak()
    {
        var engine = new StorageEngine("test-engine");
        var manager = new StateManager(storageEngine: engine);

        manager.CreateScope("s1");
        var threw = false;
        try
        {
            manager.CreateScope("s1");
        }
        catch (InvalidOperationException)
        {
            threw = true;
        }
        Assert.IsTrue(threw, "重复创建 scope 应抛 InvalidOperationException");

        // 重复创建被拒绝，且不泄漏额外会话。
        Assert.AreEqual(1, engine.GetActiveSessionIds().Count());
    }

    [TestMethod]
    public void StateManager_WithoutEngine_FallsBack_ToInMemory()
    {
        var manager = new StateManager();

        var scope = manager.CreateScope("s1");
        scope.Set("k", "v");
        Assert.AreEqual("v", scope.Get("k"));
        Assert.AreEqual("s1", manager.GetScopeNames().Single());

        Assert.IsTrue(manager.ReleaseScope("s1"));
        Assert.AreEqual(0, manager.GetScopeNames().Count());
    }
}
