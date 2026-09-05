
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using Modules.InternalStorage.Extensions;
using TConfig.ContextFrame.Abstractions;
using TConfig;
using TestInfrastructure;

namespace InternalStorage.Tests.Extensions;

[TestClass]
public class StorageCellExtensionsTests : TestBase
{
    [TestMethod]
    public void CreateChildCell_SetsParent()
    {
        var parent = new StorageCell("parent");

        var child = parent.CreateChildCell("child");

        Assert.AreSame(parent, child.Parent);
        StringAssert.Contains(child.Name, "child");
    }

    [TestMethod]
    public void CreateChildCell_AutoGeneratesName()
    {
        var parent = new StorageCell("parent");

        var child = parent.CreateChildCell();

        Assert.IsNotNull(child.Name);
        StringAssert.Contains(child.Name, parent.Name);
    }

    [TestMethod]
    public void SetConfig_StoresInConfigLayer()
    {
        var cell = new StorageCell("test");

        cell.SetConfig("key", "config-value");

        Assert.AreEqual("config-value", cell.Get<string>("key", StorageLayer.Config));
        Assert.IsNull(cell.Get<string>("key", StorageLayer.Session));
    }

    [TestMethod]
    public void SetSession_StoresInSessionLayer()
    {
        var cell = new StorageCell("test");

        cell.SetSession("key", "session-value");

        Assert.AreEqual("session-value", cell.Get<string>("key", StorageLayer.Session));
    }

    [TestMethod]
    public void SetTransient_StoresInTransientLayer()
    {
        var cell = new StorageCell("test");

        cell.SetTransient("key", "transient-value");

        Assert.AreEqual("transient-value", cell.Get<string>("key", StorageLayer.Transient));
    }

    [TestMethod]
    public void GetConfig_RetrievesFromConfigLayer()
    {
        var cell = new StorageCell("test");
        cell.Set("key", "config-value", StorageLayer.Config);

        var actual = cell.GetConfig<string>("key");

        Assert.AreEqual("config-value", actual);
    }

    [TestMethod]
    public void GetSession_RetrievesFromSessionLayer()
    {
        var cell = new StorageCell("test");
        cell.Set("key", "session-value", StorageLayer.Session);

        var actual = cell.GetSession<string>("key");

        Assert.AreEqual("session-value", actual);
    }

    [TestMethod]
    public void GetTransient_RetrievesFromTransientLayer()
    {
        var cell = new StorageCell("test");
        cell.Set("key", "transient-value", StorageLayer.Transient);

        var actual = cell.GetTransient<string>("key");

        Assert.AreEqual("transient-value", actual);
    }

    [TestMethod]
    public void SetRange_BatchSetsData()
    {
        var cell = new StorageCell("test");
        var data = new Dictionary<string, object?>
        {
            ["key1"] = "value1",
            ["key2"] = 42,
            ["key3"] = true
        };

        cell.SetRange(data, StorageLayer.Config);

        Assert.AreEqual("value1", cell.Get<string>("key1", StorageLayer.Config));
        Assert.AreEqual(42, cell.Get<int>("key2", StorageLayer.Config));
        Assert.AreEqual(true, cell.Get<bool>("key3", StorageLayer.Config));
    }

    [TestMethod]
    public void SetRange_DefaultLayerIsSession()
    {
        var cell = new StorageCell("test");
        var data = new Dictionary<string, object?>
        {
            ["key"] = "value"
        };

        cell.SetRange(data);

        Assert.AreEqual("value", cell.Get<string>("key", StorageLayer.Session));
    }

    [TestMethod]
    public void SetRange_NullData_Throws()
    {
        var cell = new StorageCell("test");

        Assert.ThrowsException<ArgumentNullException>(() =>
            cell.SetRange(null!));
    }

    [TestMethod]
    public void CollectAll_ReturnsLayerData()
    {
        var cell = new StorageCell("test");
        cell.Set("a", "value-a", StorageLayer.Session);
        cell.Set("b", "value-b", StorageLayer.Session);

        var collected = cell.CollectAll(StorageLayer.Session);

        Assert.AreEqual(2, collected.Count);
        Assert.AreEqual("value-a", collected["a"]);
        Assert.AreEqual("value-b", collected["b"]);
    }

    [TestMethod]
    public void CollectAll_EmptyLayer_ReturnsEmpty()
    {
        var cell = new StorageCell("test");

        var collected = cell.CollectAll(StorageLayer.Transient);

        Assert.AreEqual(0, collected.Count);
    }

    [TestMethod]
    public void CollectAllLayers_ReturnsAllLayers()
    {
        var cell = new StorageCell("test");
        cell.Set("cfg", "config-value", StorageLayer.Config);
        cell.Set("ses", "session-value", StorageLayer.Session);
        cell.Set("trn", "transient-value", StorageLayer.Transient);

        var allLayers = cell.CollectAllLayers();

        Assert.AreEqual(3, allLayers.Count);
        Assert.AreEqual("config-value", allLayers[StorageLayer.Config]["cfg"]);
        Assert.AreEqual("session-value", allLayers[StorageLayer.Session]["ses"]);
        Assert.AreEqual("transient-value", allLayers[StorageLayer.Transient]["trn"]);
    }

    [TestMethod]
    public void CollectAllLayers_EmptyCell_ReturnsEmptyLayers()
    {
        var cell = new StorageCell("test");

        var allLayers = cell.CollectAllLayers();

        Assert.AreEqual(3, allLayers.Count);
        Assert.AreEqual(0, allLayers[StorageLayer.Config].Count);
        Assert.AreEqual(0, allLayers[StorageLayer.Session].Count);
        Assert.AreEqual(0, allLayers[StorageLayer.Transient].Count);
    }
}
