
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using TConfig.ContextFrame.Abstractions;
using TestInfrastructure;

namespace InternalStorage.Tests.Core;

[TestClass]
public class ECSStorageTests : TestBase
{
    private IStorageCell _cell = null!;
    private IStorageRegistry _registry = null!;
    private ECSStorage _storage = null!;

    [TestInitialize]
    public override void TestInitialize()
    {
        base.TestInitialize();
        _cell = new StorageCell("test-cell");
        _registry = new StorageRegistry();
        _storage = new ECSStorage(_cell, _registry);
    }


    [TestMethod]
    public void Constructor_NullCell_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            new ECSStorage(null!, _registry));
    }

    [TestMethod]
    public void Constructor_NullRegistry_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            new ECSStorage(_cell, null!));
    }


    [TestMethod]
    public void GetEntityArchetypeId_NonExistent_ReturnsNull()
    {
        var actual = _storage.GetEntityArchetypeId(999);

        Assert.IsNull(actual);
    }

    [TestMethod]
    public void SetEntityArchetypeId_StoresValue()
    {
        var entityId = 1;
        var archetypeId = 100;

        _storage.SetEntityArchetypeId(entityId, archetypeId);

        Assert.AreEqual(archetypeId, _storage.GetEntityArchetypeId(entityId));
    }

    [TestMethod]
    public void SetEntityArchetypeId_Overwrite_UpdatesValue()
    {
        _storage.SetEntityArchetypeId(1, 100);

        _storage.SetEntityArchetypeId(1, 200);

        Assert.AreEqual(200, _storage.GetEntityArchetypeId(1));
    }

    [TestMethod]
    public void RemoveEntityArchetypeId_RemovesValue()
    {
        _storage.SetEntityArchetypeId(1, 100);

        _storage.RemoveEntityArchetypeId(1);

        Assert.IsNull(_storage.GetEntityArchetypeId(1));
    }

    [TestMethod]
    public void RemoveEntityArchetypeId_NonExistent_DoesNotThrow()
    {
        _storage.RemoveEntityArchetypeId(999);
    }

    [TestMethod]
    public void GetAllEntityArchetypeIds_ReturnsAll()
    {
        _storage.SetEntityArchetypeId(1, 100);
        _storage.SetEntityArchetypeId(2, 200);
        _storage.SetEntityArchetypeId(3, 100);

        var all = _storage.GetAllEntityArchetypeIds().ToList();

        Assert.AreEqual(3, all.Count);
        CollectionAssert.Contains(all, new KeyValuePair<int, int>(1, 100));
        CollectionAssert.Contains(all, new KeyValuePair<int, int>(2, 200));
        CollectionAssert.Contains(all, new KeyValuePair<int, int>(3, 100));
    }

    [TestMethod]
    public void GetAllEntityArchetypeIds_Empty_ReturnsEmpty()
    {
        var all = _storage.GetAllEntityArchetypeIds().ToList();

        Assert.AreEqual(0, all.Count);
    }


    [TestMethod]
    public void GetArchetypeData_NonExistent_ReturnsNull()
    {
        var actual = _storage.GetArchetypeData(999);

        Assert.IsNull(actual);
    }

    [TestMethod]
    public void SetArchetypeData_StoresData()
    {
        var hash = 12345;
        var data = new byte[] { 0x01, 0x02, 0x03 };

        _storage.SetArchetypeData(hash, data);

        var actual = _storage.GetArchetypeData(hash);
        Assert.IsNotNull(actual);
        CollectionAssert.AreEqual(data, actual);
    }

    [TestMethod]
    public void SetArchetypeData_Overwrite_UpdatesData()
    {
        _storage.SetArchetypeData(12345, new byte[] { 0x01 });

        _storage.SetArchetypeData(12345, new byte[] { 0xFF });

        var actual = _storage.GetArchetypeData(12345);
        Assert.IsNotNull(actual);
        Assert.AreEqual(0xFF, actual[0]);
    }

    [TestMethod]
    public void GetAllArchetypeSignatureHashes_ReturnsAll()
    {
        _storage.SetArchetypeData(100, new byte[] { 0x01 });
        _storage.SetArchetypeData(200, new byte[] { 0x02 });

        var hashes = _storage.GetAllArchetypeSignatureHashes().ToList();

        Assert.AreEqual(2, hashes.Count);
        CollectionAssert.Contains(hashes, 100);
        CollectionAssert.Contains(hashes, 200);
    }

    [TestMethod]
    public void GetAllArchetypeSignatureHashes_Empty_ReturnsEmpty()
    {
        var hashes = _storage.GetAllArchetypeSignatureHashes().ToList();

        Assert.AreEqual(0, hashes.Count);
    }


    [TestMethod]
    public void GetFreeEntityIds_Empty_ReturnsEmpty()
    {
        var ids = _storage.GetFreeEntityIds().ToList();

        Assert.AreEqual(0, ids.Count);
    }

    [TestMethod]
    public void AddFreeEntityId_AddsId()
    {
        _storage.AddFreeEntityId(42);

        var ids = _storage.GetFreeEntityIds().ToList();

        Assert.AreEqual(1, ids.Count);
        Assert.AreEqual(42, ids[0]);
    }

    [TestMethod]
    public void AddFreeEntityId_Multiple_AddsAll()
    {
        _storage.AddFreeEntityId(1);
        _storage.AddFreeEntityId(2);
        _storage.AddFreeEntityId(3);

        var ids = _storage.GetFreeEntityIds().ToList();

        Assert.AreEqual(3, ids.Count);
        CollectionAssert.Contains(ids, 1);
        CollectionAssert.Contains(ids, 2);
        CollectionAssert.Contains(ids, 3);
    }

    [TestMethod]
    public void TakeFreeEntityId_TakesFirst()
    {
        _storage.AddFreeEntityId(10);
        _storage.AddFreeEntityId(20);

        var taken = _storage.TakeFreeEntityId();

        Assert.IsNotNull(taken);
        var remaining = _storage.GetFreeEntityIds().ToList();
        Assert.AreEqual(1, remaining.Count);
    }

    [TestMethod]
    public void TakeFreeEntityId_Empty_ReturnsNull()
    {
        var taken = _storage.TakeFreeEntityId();

        Assert.IsNull(taken);
    }

    [TestMethod]
    public void GetEntityVersion_Default_ReturnsZero()
    {
        var version = _storage.GetEntityVersion(1);

        Assert.AreEqual(0, version);
    }

    [TestMethod]
    public void SetEntityVersion_SetsVersion()
    {
        _storage.SetEntityVersion(1, 5);

        var version = _storage.GetEntityVersion(1);

        Assert.AreEqual(5, version);
    }

    [TestMethod]
    public void SetEntityVersion_Overwrite_UpdatesVersion()
    {
        _storage.SetEntityVersion(1, 5);

        _storage.SetEntityVersion(1, 10);

        Assert.AreEqual(10, _storage.GetEntityVersion(1));
    }


    [TestMethod]
    public void GetChunkColumn_NonExistent_ReturnsNull()
    {
        var column = _storage.GetChunkColumn(1, 100);

        Assert.IsNull(column);
    }

    [TestMethod]
    public void SetChunkColumn_SetsColumn()
    {
        var chunkId = 1;
        var componentTypeId = 100;
        var column = new int[] { 10, 20, 30 };

        _storage.SetChunkColumn(chunkId, componentTypeId, column);

        var actual = _storage.GetChunkColumn(chunkId, componentTypeId);
        Assert.IsNotNull(actual);
        CollectionAssert.AreEqual(column, (int[])actual);
    }

    [TestMethod]
    public void SetChunkColumn_Overwrite_UpdatesColumn()
    {
        _storage.SetChunkColumn(1, 100, new int[] { 1, 2, 3 });

        _storage.SetChunkColumn(1, 100, new int[] { 4, 5, 6 });

        var actual = (int[])_storage.GetChunkColumn(1, 100)!;
        Assert.AreEqual(4, actual[0]);
        Assert.AreEqual(5, actual[1]);
        Assert.AreEqual(6, actual[2]);
    }

    [TestMethod]
    public void GetChunkEntityIds_NonExistent_ReturnsNull()
    {
        var ids = _storage.GetChunkEntityIds(1);

        Assert.IsNull(ids);
    }

    [TestMethod]
    public void SetChunkEntityIds_SetsIds()
    {
        var chunkId = 1;
        var entityIds = new int[] { 101, 102, 103 };

        _storage.SetChunkEntityIds(chunkId, entityIds);

        var actual = _storage.GetChunkEntityIds(chunkId);
        Assert.IsNotNull(actual);
        CollectionAssert.AreEqual(entityIds, actual);
    }

    [TestMethod]
    public void SetChunkEntityIds_Overwrite_UpdatesIds()
    {
        _storage.SetChunkEntityIds(1, new int[] { 1, 2 });

        _storage.SetChunkEntityIds(1, new int[] { 3, 4, 5 });

        var actual = _storage.GetChunkEntityIds(1);
        Assert.IsNotNull(actual);
        Assert.AreEqual(3, actual.Length);
        Assert.AreEqual(3, actual[0]);
        Assert.AreEqual(5, actual[2]);
    }


    [TestMethod]
    public void MultipleChunks_AreIsolated()
    {
        _storage.SetChunkColumn(1, 100, new int[] { 1, 2 });
        _storage.SetChunkColumn(2, 100, new int[] { 3, 4 });
        _storage.SetChunkEntityIds(1, new int[] { 101, 102 });
        _storage.SetChunkEntityIds(2, new int[] { 103, 104 });

        var col1 = (int[])_storage.GetChunkColumn(1, 100)!;
        var col2 = (int[])_storage.GetChunkColumn(2, 100)!;
        Assert.AreEqual(1, col1[0]);
        Assert.AreEqual(3, col2[0]);

        var ids1 = _storage.GetChunkEntityIds(1)!;
        var ids2 = _storage.GetChunkEntityIds(2)!;
        Assert.AreEqual(101, ids1[0]);
        Assert.AreEqual(103, ids2[0]);
    }
}
