
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using TConfig.ContextFrame.Abstractions;
using TConfig;
using TestInfrastructure;

namespace InternalStorage.Tests.Core;

[TestClass]
public class StorageCellTests : TestBase
{
    [TestMethod]
    public void Set_Get_SameLayer_ReturnsValue()
    {
        var cell = new StorageCell("test");
        var expected = "hello";

        cell.Set("key1", expected, StorageLayer.Session);
        var actual = cell.Get<string>("key1", StorageLayer.Session);

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void Set_Get_DifferentLayers_Isolated()
    {
        var cell = new StorageCell("test");

        cell.Set("key", "config-value", StorageLayer.Config);
        cell.Set("key", "session-value", StorageLayer.Session);
        cell.Set("key", "transient-value", StorageLayer.Transient);

        Assert.AreEqual("config-value", cell.Get<string>("key", StorageLayer.Config));
        Assert.AreEqual("session-value", cell.Get<string>("key", StorageLayer.Session));
        Assert.AreEqual("transient-value", cell.Get<string>("key", StorageLayer.Transient));
    }

    [TestMethod]
    public void Get_CrossLayer_FindsInOrder()
    {
        var cell = new StorageCell("test");

        cell.Set("key", "config-value", StorageLayer.Config);

        var actual = cell.Get<string>("key");

        Assert.AreEqual("config-value", actual);
    }

    [TestMethod]
    public void Get_CrossLayer_PrefersConfigOverSession()
    {
        var cell = new StorageCell("test");
        cell.Set("key", "config-value", StorageLayer.Config);
        cell.Set("key", "session-value", StorageLayer.Session);

        var actual = cell.Get<string>("key");

        Assert.AreEqual("config-value", actual);
    }

    [TestMethod]
    public void Get_SpecifiedLayer_DoesNotFallbackToParent()
    {
        var parent = new StorageCell("parent");
        parent.Set("parent-key", "parent-value", StorageLayer.Config);

        var child = new StorageCell("child", parent);

        var actual = child.Get<string>("parent-key", StorageLayer.Session);

        Assert.IsNull(actual);
    }

    [TestMethod]
    public void Get_WithParent_CascadesToParent()
    {
        var parent = new StorageCell("parent");
        parent.Set("parent-key", "parent-value", StorageLayer.Config);

        var child = new StorageCell("child", parent);

        var actual = child.Get<string>("parent-key");

        Assert.AreEqual("parent-value", actual);
    }

    [TestMethod]
    public void Get_WithParent_ChildOverridesParent()
    {
        var parent = new StorageCell("parent");
        parent.Set("key", "parent-value", StorageLayer.Config);

        var child = new StorageCell("child", parent);
        child.Set("key", "child-value", StorageLayer.Session);

        var actual = child.Get<string>("key");

        Assert.AreEqual("child-value", actual);
    }

    [TestMethod]
    public void TryGet_ReturnsTrue_WhenExists()
    {
        var cell = new StorageCell("test");
        cell.Set("key", 42, StorageLayer.Session);

        var result = cell.TryGet("key", out int value);

        Assert.IsTrue(result);
        Assert.AreEqual(42, value);
    }

    [TestMethod]
    public void TryGet_ReturnsFalse_WhenNotExists()
    {
        var cell = new StorageCell("test");

        var result = cell.TryGet("nonexistent", out string? value);

        Assert.IsFalse(result);
        Assert.IsNull(value);
    }

    [TestMethod]
    public void Set_TriggersDataChanged_Added()
    {
        var cell = new StorageCell("test");
        var changedKey = string.Empty;
        DataChangeType? changeType = null;
        cell.DataChanged += (_, e) =>
        {
            changedKey = e.Key;
            changeType = e.ChangeType;
        };

        cell.Set("key", "value", StorageLayer.Session);

        Assert.AreEqual("key", changedKey);
        Assert.AreEqual(DataChangeType.Added, changeType);
    }

    [TestMethod]
    public void Set_TriggersDataChanged_Updated()
    {
        var cell = new StorageCell("test");
        cell.Set("key", "old-value", StorageLayer.Session);

        var changedKey = string.Empty;
        DataChangeType? changeType = null;
        cell.DataChanged += (_, e) =>
        {
            changedKey = e.Key;
            changeType = e.ChangeType;
        };

        cell.Set("key", "new-value", StorageLayer.Session);

        Assert.AreEqual("key", changedKey);
        Assert.AreEqual(DataChangeType.Updated, changeType);
    }

    [TestMethod]
    public void Remove_RemovesFromAllLayers()
    {
        var cell = new StorageCell("test");
        cell.Set("key", "config", StorageLayer.Config);
        cell.Set("key", "session", StorageLayer.Session);
        cell.Set("key", "transient", StorageLayer.Transient);

        var removed = cell.Remove("key");

        Assert.IsTrue(removed);
        Assert.IsFalse(cell.Contains("key"));
    }

    [TestMethod]
    public void Remove_SpecifiedLayer_OnlyThatLayer()
    {
        var cell = new StorageCell("test");
        cell.Set("key", "config", StorageLayer.Config);
        cell.Set("key", "session", StorageLayer.Session);

        var removed = cell.Remove("key", StorageLayer.Session);

        Assert.IsTrue(removed);
        Assert.IsTrue(cell.Contains("key", StorageLayer.Config));
        Assert.IsFalse(cell.Contains("key", StorageLayer.Session));
    }

    [TestMethod]
    public void Remove_TriggersDataChanged_Removed()
    {
        var cell = new StorageCell("test");
        cell.Set("key", "value", StorageLayer.Session);

        var changedKey = string.Empty;
        DataChangeType? changeType = null;
        cell.DataChanged += (_, e) =>
        {
            changedKey = e.Key;
            changeType = e.ChangeType;
        };

        cell.Remove("key", StorageLayer.Session);

        Assert.AreEqual("key", changedKey);
        Assert.AreEqual(DataChangeType.Removed, changeType);
    }

    [TestMethod]
    public void Contains_ChecksAllLayers()
    {
        var cell = new StorageCell("test");
        cell.Set("key", "value", StorageLayer.Transient);

        Assert.IsTrue(cell.Contains("key"));
        Assert.IsFalse(cell.Contains("key", StorageLayer.Config));
        Assert.IsFalse(cell.Contains("key", StorageLayer.Session));
        Assert.IsTrue(cell.Contains("key", StorageLayer.Transient));
    }

    [TestMethod]
    public void ClearLayer_RemovesAllEntries()
    {
        var cell = new StorageCell("test");
        cell.Set("key1", "value1", StorageLayer.Session);
        cell.Set("key2", "value2", StorageLayer.Session);

        cell.ClearLayer(StorageLayer.Session);

        Assert.AreEqual(0, cell.GetKeys(StorageLayer.Session).Count());
    }

    [TestMethod]
    public void ClearLayer_TriggersRemoveEvents()
    {
        var cell = new StorageCell("test");
        cell.Set("key1", "value1", StorageLayer.Session);
        cell.Set("key2", "value2", StorageLayer.Session);

        var removedCount = 0;
        cell.DataChanged += (_, e) =>
        {
            if (e.ChangeType == DataChangeType.Removed)
                removedCount++;
        };

        cell.ClearLayer(StorageLayer.Session);

        Assert.AreEqual(2, removedCount);
    }

    [TestMethod]
    public void SetObject_DelegatesToSet()
    {
        var cell = new StorageCell("test");

        cell.SetObject("key", "object-value", StorageLayer.Config);
        var actual = cell.Get<string>("key", StorageLayer.Config);

        Assert.AreEqual("object-value", actual);
    }

    [TestMethod]
    public void SetObject_SupportsNullValue()
    {
        var cell = new StorageCell("test");

        cell.SetObject("key", null, StorageLayer.Session);
        var actual = cell.Get<object?>("key", StorageLayer.Session);

        Assert.IsNull(actual);
    }

    [TestMethod]
    public void Set_Overwrite_ChangesValue()
    {
        var cell = new StorageCell("test");
        cell.Set("key", "original", StorageLayer.Session);

        cell.Set("key", "updated", StorageLayer.Session);
        var actual = cell.Get<string>("key", StorageLayer.Session);

        Assert.AreEqual("updated", actual);
    }

    [TestMethod]
    public void GetEntries_ReturnsLayerEntries()
    {
        var cell = new StorageCell("test");
        cell.Set("key1", "value1", StorageLayer.Config);
        cell.Set("key2", "value2", StorageLayer.Config);

        var entries = cell.GetEntries(StorageLayer.Config).ToList();

        Assert.AreEqual(2, entries.Count);
        Assert.IsTrue(entries.Any(e => e.Key == "key1"));
        Assert.IsTrue(entries.Any(e => e.Key == "key2"));
    }

    [TestMethod]
    public void GetKeys_ReturnsLayerKeys()
    {
        var cell = new StorageCell("test");
        cell.Set("a", 1, StorageLayer.Transient);
        cell.Set("b", 2, StorageLayer.Transient);

        var keys = cell.GetKeys(StorageLayer.Transient).ToList();

        Assert.AreEqual(2, keys.Count);
        CollectionAssert.Contains(keys, "a");
        CollectionAssert.Contains(keys, "b");
    }

    [TestMethod]
    public void Name_ReturnsProvidedName()
    {
        var cell = new StorageCell("my-cell");

        Assert.AreEqual("my-cell", cell.Name);
    }

    [TestMethod]
    public void Name_AutoGenerated_WhenNotProvided()
    {
        var cell = new StorageCell();

        Assert.IsNotNull(cell.Name);
        StringAssert.StartsWith(cell.Name, "StorageCell-");
    }

    [TestMethod]
    public void Parent_CanBeSetAndGet()
    {
        var parent = new StorageCell("parent");
        var child = new StorageCell("child");

        child.Parent = parent;

        Assert.AreSame(parent, child.Parent);
    }
}
