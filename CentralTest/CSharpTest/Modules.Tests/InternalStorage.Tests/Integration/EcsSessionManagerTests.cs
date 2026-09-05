
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using Modules.InternalStorage.Integration;
using TConfig.ContextFrame.Abstractions;
using TestInfrastructure;

namespace InternalStorage.Tests.Integration;

[TestClass]
public class EcsSessionManagerTests : TestBase
{
    private IStorageEngine _engine = null!;
    private EcsSessionManager _manager = null!;

    [TestInitialize]
    public override void TestInitialize()
    {
        base.TestInitialize();
        _engine = new StorageEngine("test-engine");
        _manager = new EcsSessionManager(_engine);
    }

    [TestCleanup]
    public override void TestCleanup()
    {
        _manager.Dispose();
        base.TestCleanup();
    }


    [TestMethod]
    public void Constructor_NullEngine_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            new EcsSessionManager(null!));
    }

    [TestMethod]
    public void Constructor_EngineProperty_IsSet()
    {
        Assert.AreSame(_engine, _manager.Engine);
    }


    [TestMethod]
    public void RegisterEcsFrame_ReturnsCell()
    {
        var cell = _manager.RegisterEcsFrame("mainForm", new object());

        Assert.IsNotNull(cell);
    }

    [TestMethod]
    public void RegisterEcsFrame_Multiple_ReturnsDifferentCells()
    {
        var cell1 = _manager.RegisterEcsFrame("form-1", new object());
        var cell2 = _manager.RegisterEcsFrame("form-2", new object());

        Assert.IsNotNull(cell1);
        Assert.IsNotNull(cell2);
        Assert.AreNotSame(cell1, cell2);
    }

    [TestMethod]
    public void RegisterEcsFrame_NullSessionId_Throws()
    {
        Assert.ThrowsException<ArgumentNullException>(() =>
            _manager.RegisterEcsFrame(null!, new object()));
    }

    [TestMethod]
    public void RegisterEcsFrame_EmptySessionId_Throws()
    {
        Assert.ThrowsException<ArgumentException>(() =>
            _manager.RegisterEcsFrame(string.Empty, new object()));
    }


    [TestMethod]
    public void UnregisterEcsFrame_RemovesSession()
    {
        _manager.RegisterEcsFrame("form-1", new object());

        _manager.UnregisterEcsFrame("form-1");

        Assert.IsFalse(_engine.GetActiveSessionIds().Contains("form-1"));
    }

    [TestMethod]
    public void UnregisterEcsFrame_NonExistent_DoesNotThrow()
    {
        _manager.UnregisterEcsFrame("nonexistent");
    }

    [TestMethod]
    public void UnregisterEcsFrame_ReducesSessionCount()
    {
        _manager.RegisterEcsFrame("form-1", new object());
        _manager.RegisterEcsFrame("form-2", new object());

        _manager.UnregisterEcsFrame("form-1");

        var activeIds = _engine.GetActiveSessionIds().ToList();
        Assert.AreEqual(1, activeIds.Count);
        Assert.AreEqual("form-2", activeIds[0]);
    }


    [TestMethod]
    public void SyncComponentData_StoresData()
    {
        _manager.RegisterEcsFrame("form-1", new object());
        var position = new { X = 10, Y = 20 };

        _manager.SyncComponentData("form-1", "position", position);

        var actual = _manager.ReadComponentData<object>("form-1", "position");
        Assert.IsNotNull(actual);
    }

    [TestMethod]
    public void SyncComponentData_MultipleComponents_StoresAll()
    {
        _manager.RegisterEcsFrame("form-1", new object());

        _manager.SyncComponentData("form-1", "position", new { X = 10 });
        _manager.SyncComponentData("form-1", "velocity", new { Vx = 5 });

        Assert.IsNotNull(_manager.ReadComponentData<object>("form-1", "position"));
        Assert.IsNotNull(_manager.ReadComponentData<object>("form-1", "velocity"));
    }

    [TestMethod]
    public void SyncComponentData_Overwrite_UpdatesData()
    {
        _manager.RegisterEcsFrame("form-1", new object());
        _manager.SyncComponentData("form-1", "position", new { X = 10 });

        _manager.SyncComponentData("form-1", "position", new { X = 99 });

        var actual = _manager.ReadComponentData<object>("form-1", "position");
        Assert.IsNotNull(actual);
    }

    [TestMethod]
    public void ReadComponentData_NonExistent_ReturnsDefault()
    {
        _manager.RegisterEcsFrame("form-1", new object());

        var actual = _manager.ReadComponentData<string>("form-1", "nonexistent");

        Assert.IsNull(actual);
    }

    [TestMethod]
    public void ReadComponentData_NonExistentSession_ReturnsDefault()
    {
        var actual = _manager.ReadComponentData<string>("nonexistent", "key");

        Assert.IsNull(actual);
    }


    [TestMethod]
    public void GetSyncedComponents_ReturnsAll()
    {
        _manager.RegisterEcsFrame("form-1", new object());
        _manager.SyncComponentData("form-1", "position", new { X = 10 });
        _manager.SyncComponentData("form-1", "velocity", new { Vx = 5 });

        var components = _manager.GetSyncedComponents("form-1");

        Assert.AreEqual(2, components.Count);
        Assert.IsTrue(components.Contains("position"));
        Assert.IsTrue(components.Contains("velocity"));
    }

    [TestMethod]
    public void GetSyncedComponents_Empty_ReturnsEmpty()
    {
        _manager.RegisterEcsFrame("form-1", new object());

        var components = _manager.GetSyncedComponents("form-1");

        Assert.AreEqual(0, components.Count);
    }

    [TestMethod]
    public void GetSyncedComponents_NonExistentSession_ReturnsEmpty()
    {
        var components = _manager.GetSyncedComponents("nonexistent");

        Assert.AreEqual(0, components.Count);
    }


    [TestMethod]
    public void GetActiveSessionIds_ReturnsAll()
    {
        _manager.RegisterEcsFrame("form-1", new object());
        _manager.RegisterEcsFrame("form-2", new object());

        var ids = _manager.GetActiveSessionIds().ToList();

        Assert.AreEqual(2, ids.Count);
        Assert.IsTrue(ids.Contains("form-1"));
        Assert.IsTrue(ids.Contains("form-2"));
    }

    [TestMethod]
    public void GetActiveSessionIds_Empty_ReturnsEmpty()
    {
        var ids = _manager.GetActiveSessionIds().ToList();

        Assert.AreEqual(0, ids.Count);
    }


    [TestMethod]
    public void SaveSnapshot_ReturnsSnapshot()
    {
        _manager.RegisterEcsFrame("form-1", new object());
        _manager.SyncComponentData("form-1", "position", new { X = 10 });

        var snapshot = _manager.SaveSnapshot("form-1");

        Assert.IsNotNull(snapshot);
        Assert.IsFalse(string.IsNullOrEmpty(snapshot.Id));
    }

    [TestMethod]
    public void RestoreSnapshot_RestoresData()
    {
        _manager.RegisterEcsFrame("form-1", new object());
        _manager.SyncComponentData("form-1", "position", new { X = 10 });
        var snapshot = _manager.SaveSnapshot("form-1");

        _manager.SyncComponentData("form-1", "position", new { X = 99 });
        _manager.RestoreSnapshot("form-1", snapshot);

        var restored = _manager.ReadComponentData<object>("form-1", "position");
        Assert.IsNotNull(restored);
    }


    [TestMethod]
    public void Sessions_DataIsIsolated()
    {
        _manager.RegisterEcsFrame("form-1", new object());
        _manager.RegisterEcsFrame("form-2", new object());

        _manager.SyncComponentData("form-1", "position", new { X = 10 });

        Assert.IsNotNull(_manager.ReadComponentData<object>("form-1", "position"));
        Assert.IsNull(_manager.ReadComponentData<object>("form-2", "position"));
    }


    [TestMethod]
    public void Dispose_ClearsAll()
    {
        _manager.RegisterEcsFrame("form-1", new object());
        _manager.RegisterEcsFrame("form-2", new object());

        _manager.Dispose();

        Assert.AreEqual(0, _manager.GetActiveSessionIds().Count());
    }

    [TestMethod]
    public void Dispose_MultipleCalls_DoesNotThrow()
    {
        _manager.RegisterEcsFrame("form-1", new object());

        _manager.Dispose();
        _manager.Dispose();
    }
}
