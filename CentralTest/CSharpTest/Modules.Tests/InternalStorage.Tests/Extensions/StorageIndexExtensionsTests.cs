
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using Modules.InternalStorage.Extensions;
using TConfig.ContextFrame.Abstractions;
using TConfig;
using TestInfrastructure;

namespace InternalStorage.Tests.Extensions;

[TestClass]
public class StorageIndexExtensionsTests : TestBase
{
    private IStorageCell _cell = null!;

    [TestInitialize]
    public override void TestInitialize()
    {
        base.TestInitialize();
        _cell = new StorageCell("test-cell");
    }


    [TestMethod]
    public void AddUniqueIndex_StoresValue()
    {
        const string indexName = "name-index";

        _cell.AddUniqueIndex<string, string>(indexName, "entity-1", "value-1", StorageLayer.Session);

        var actual = _cell.FindByIndex(indexName, "entity-1");
        Assert.AreEqual("value-1", actual);
    }

    [TestMethod]
    public void AddUniqueIndex_DuplicateKey_Throws()
    {
        const string indexName = "name-index";
        _cell.AddUniqueIndex<string, string>(indexName, "entity-1", "value-1", StorageLayer.Session);

        Assert.ThrowsException<InvalidOperationException>(() =>
            _cell.AddUniqueIndex<string, string>(indexName, "entity-1", "value-2", StorageLayer.Session));
    }

    [TestMethod]
    public void AddUniqueIndex_SameKeyDifferentIndexName_Allowed()
    {
        _cell.AddUniqueIndex<string, string>("index-1", "key", "value-1", StorageLayer.Session);

        _cell.AddUniqueIndex<string, string>("index-2", "key", "value-2", StorageLayer.Session);

        Assert.AreEqual("value-1", _cell.FindByIndex("index-1", "key"));
        Assert.AreEqual("value-2", _cell.FindByIndex("index-2", "key"));
    }


    [TestMethod]
    public void IndexAdd_NonUnique_AddsToSet()
    {
        const string indexName = "tag-index";
        _cell.RegisterIndex(indexName, "EntityType", false, StorageLayer.Session);

        _cell.IndexAdd(indexName, "tag-A", "entity-1");
        _cell.IndexAdd(indexName, "tag-A", "entity-2");

        var results = _cell.FindAllByIndex(indexName, "tag-A").ToList();
        Assert.AreEqual(2, results.Count);
        CollectionAssert.Contains(results, "entity-1");
        CollectionAssert.Contains(results, "entity-2");
    }

    [TestMethod]
    public void IndexAdd_NonUnique_Duplicate_DoesNotDuplicate()
    {
        const string indexName = "tag-index";
        _cell.RegisterIndex(indexName, "EntityType", false, StorageLayer.Session);
        _cell.IndexAdd(indexName, "tag-A", "entity-1");

        _cell.IndexAdd(indexName, "tag-A", "entity-1");

        var results = _cell.FindAllByIndex(indexName, "tag-A").ToList();
        Assert.AreEqual(1, results.Count);
    }


    [TestMethod]
    public void IndexRemove_Unique_RemovesValue()
    {
        const string indexName = "name-index";
        _cell.AddUniqueIndex<string, string>(indexName, "entity-1", "value-1", StorageLayer.Session);

        _cell.IndexRemove(indexName, "entity-1");

        Assert.IsNull(_cell.FindByIndex(indexName, "entity-1"));
    }

    [TestMethod]
    public void IndexRemove_NonUnique_RemovesFromSet()
    {
        const string indexName = "tag-index";
        _cell.RegisterIndex(indexName, "EntityType", false, StorageLayer.Session);
        _cell.IndexAdd(indexName, "tag-A", "entity-1");
        _cell.IndexAdd(indexName, "tag-A", "entity-2");

        _cell.IndexRemove(indexName, "tag-A", "entity-1");

        var results = _cell.FindAllByIndex(indexName, "tag-A").ToList();
        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("entity-2", results[0]);
    }

    [TestMethod]
    public void IndexRemove_NonExistent_DoesNotThrow()
    {
        const string indexName = "name-index";
        _cell.AddUniqueIndex<string, string>(indexName, "entity-1", "value-1", StorageLayer.Session);

        _cell.IndexRemove(indexName, "nonexistent");
    }

    [TestMethod]
    public void IndexUpdate_UpdatesIndex()
    {
        const string indexName = "name-index";
        _cell.AddUniqueIndex<string, string>(indexName, "old-key", "entity-1", StorageLayer.Session);

        _cell.IndexUpdate(indexName, "old-key", "new-key", "entity-1");

        Assert.IsNull(_cell.FindByIndex(indexName, "old-key"));
        Assert.AreEqual("entity-1", _cell.FindByIndex(indexName, "new-key"));
    }

    [TestMethod]
    public void IndexUpdate_SameKey_DoesNothing()
    {
        const string indexName = "name-index";
        _cell.AddUniqueIndex<string, string>(indexName, "key", "entity-1", StorageLayer.Session);

        _cell.IndexUpdate(indexName, "key", "key", "entity-1");

        Assert.AreEqual("entity-1", _cell.FindByIndex(indexName, "key"));
    }


    [TestMethod]
    public void FindByIndex_Unique_ReturnsValue()
    {
        const string indexName = "name-index";
        _cell.AddUniqueIndex<string, string>(indexName, "entity-1", "value-1", StorageLayer.Session);

        var actual = _cell.FindByIndex(indexName, "entity-1");

        Assert.AreEqual("value-1", actual);
    }

    [TestMethod]
    public void FindByIndex_NonExistent_ReturnsNull()
    {
        const string indexName = "name-index";
        _cell.AddUniqueIndex<string, string>(indexName, "entity-1", "value-1", StorageLayer.Session);

        var actual = _cell.FindByIndex(indexName, "nonexistent");

        Assert.IsNull(actual);
    }

    [TestMethod]
    public void FindAllByIndex_NonUnique_ReturnsAll()
    {
        const string indexName = "tag-index";
        _cell.RegisterIndex(indexName, "EntityType", false, StorageLayer.Session);
        _cell.IndexAdd(indexName, "tag-A", "entity-1");
        _cell.IndexAdd(indexName, "tag-A", "entity-2");
        _cell.IndexAdd(indexName, "tag-B", "entity-3");

        var results = _cell.FindAllByIndex(indexName, "tag-A").ToList();

        Assert.AreEqual(2, results.Count);
        CollectionAssert.Contains(results, "entity-1");
        CollectionAssert.Contains(results, "entity-2");
    }

    [TestMethod]
    public void FindAllByIndex_NonExistent_ReturnsEmpty()
    {
        const string indexName = "tag-index";
        _cell.RegisterIndex(indexName, "EntityType", false, StorageLayer.Session);

        var results = _cell.FindAllByIndex(indexName, "nonexistent").ToList();

        Assert.AreEqual(0, results.Count);
    }


    [TestMethod]
    public void IndexContains_Unique_ReturnsTrue_WhenExists()
    {
        const string indexName = "name-index";
        _cell.AddUniqueIndex<string, string>(indexName, "entity-1", "value-1", StorageLayer.Session);

        Assert.IsTrue(_cell.IndexContains(indexName, "entity-1"));
    }

    [TestMethod]
    public void IndexContains_Unique_ReturnsFalse_WhenNotExists()
    {
        const string indexName = "name-index";
        _cell.AddUniqueIndex<string, string>(indexName, "entity-1", "value-1", StorageLayer.Session);

        Assert.IsFalse(_cell.IndexContains(indexName, "nonexistent"));
    }

    [TestMethod]
    public void IndexContains_NonUnique_ReturnsTrue_WhenExists()
    {
        const string indexName = "tag-index";
        _cell.RegisterIndex(indexName, "EntityType", false, StorageLayer.Session);
        _cell.IndexAdd(indexName, "tag-A", "entity-1");

        Assert.IsTrue(_cell.IndexContains(indexName, "tag-A"));
    }

    [TestMethod]
    public void IndexContains_NonUnique_ReturnsFalse_WhenNotExists()
    {
        const string indexName = "tag-index";
        _cell.RegisterIndex(indexName, "EntityType", false, StorageLayer.Session);

        Assert.IsFalse(_cell.IndexContains(indexName, "nonexistent"));
    }


    [TestMethod]
    public void IndexAdd_Unregistered_Throws()
    {
        const string indexName = "unregistered-index";

        Assert.ThrowsException<InvalidOperationException>(() =>
            _cell.IndexAdd(indexName, "key", "entity-1"));
    }

    [TestMethod]
    public void FindByIndex_Unregistered_ReturnsNull()
    {
        const string indexName = "unregistered-index";

        var actual = _cell.FindByIndex(indexName, "key");

        Assert.IsNull(actual);
    }

    [TestMethod]
    public void FindAllByIndex_Unregistered_ReturnsEmpty()
    {
        const string indexName = "unregistered-index";

        var results = _cell.FindAllByIndex(indexName, "key").ToList();

        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void IndexContains_Unregistered_ReturnsFalse()
    {
        const string indexName = "unregistered-index";

        Assert.IsFalse(_cell.IndexContains(indexName, "key"));
    }


    [TestMethod]
    public void Indexes_DifferentLayers_AreIsolated()
    {
        const string indexName = "name-index";
        _cell.AddUniqueIndex<string, string>(indexName, "key", "session-value", StorageLayer.Session);
        _cell.AddUniqueIndex<string, string>(indexName, "key", "config-value", StorageLayer.Config);

        Assert.AreEqual("session-value", _cell.FindByIndex(indexName, "key", StorageLayer.Session));
        Assert.AreEqual("config-value", _cell.FindByIndex(indexName, "key", StorageLayer.Config));
    }
}
