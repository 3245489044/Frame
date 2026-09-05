
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using TConfig.ContextFrame.Abstractions;
using TConfig;
using TestInfrastructure;

namespace InternalStorage.Tests.Core;

[TestClass]
public class SnapshotManagerTests : TestBase
{
    private SnapshotManager _manager = null!;
    private StorageCell _cell = null!;

    [TestInitialize]
    public override void TestInitialize()
    {
        base.TestInitialize();
        _manager = new SnapshotManager();
        _cell = new StorageCell("test-cell");
    }

    [TestMethod]
    public void CreateSnapshot_ContainsSessionData()
    {
        _cell.Set("key1", "value1", StorageLayer.Session);
        _cell.Set("key2", "value2", StorageLayer.Session);

        var snapshot = _manager.CreateSnapshot(_cell);

        Assert.AreEqual(2, snapshot.SessionData.Count);
        Assert.AreEqual("value1", snapshot.SessionData["key1"]);
        Assert.AreEqual("value2", snapshot.SessionData["key2"]);
    }

    [TestMethod]
    public void CreateSnapshot_ExcludesTransient()
    {
        _cell.Set("session-key", "session-value", StorageLayer.Session);
        _cell.Set("transient-key", "transient-value", StorageLayer.Transient);

        var snapshot = _manager.CreateSnapshot(_cell);

        Assert.AreEqual(1, snapshot.SessionData.Count);
        Assert.IsTrue(snapshot.SessionData.ContainsKey("session-key"));
        Assert.IsFalse(snapshot.SessionData.ContainsKey("transient-key"));
    }

    [TestMethod]
    public void CreateSnapshot_IncludesPreserveEntries()
    {
        _cell.Set("preserve-key", "preserve-value", StorageLayer.Session);

        var snapshot = _manager.CreateSnapshot(_cell);

        Assert.AreEqual(1, snapshot.SessionData.Count);
        Assert.AreEqual("preserve-value", snapshot.SessionData["preserve-key"]);
    }

    [TestMethod]
    public void CreateSnapshot_ExcludesExcludeEntries()
    {
        _cell.Set("transient-key", "transient-value", StorageLayer.Transient);

        _cell.Set("session-key", "session-value", StorageLayer.Session);

        var snapshot = _manager.CreateSnapshot(_cell);

        Assert.AreEqual(1, snapshot.SessionData.Count);
        Assert.IsTrue(snapshot.SessionData.ContainsKey("session-key"));
        Assert.IsFalse(snapshot.SessionData.ContainsKey("transient-key"));
    }

    [TestMethod]
    public void RestoreSnapshot_RestoresSessionData()
    {
        _cell.Set("key", "original", StorageLayer.Session);
        var snapshot = _manager.CreateSnapshot(_cell);

        _cell.Set("key", "modified", StorageLayer.Session);

        _manager.RestoreSnapshot(_cell, snapshot);

        Assert.AreEqual("original", _cell.Get<string>("key", StorageLayer.Session));
    }

    [TestMethod]
    public void RestoreSnapshot_PreservesPreserveData()
    {
        _cell.Set("preserve-key", "preserve-value", StorageLayer.Config);
        var snapshot = _manager.CreateSnapshot(_cell);

        _cell.Set("preserve-key", "modified-value", StorageLayer.Config);

        _manager.RestoreSnapshot(_cell, snapshot);

        Assert.AreEqual("modified-value",
            _cell.Get<string>("preserve-key", StorageLayer.Config));
    }

    [TestMethod]
    public void RestoreSnapshot_DoesNotResurrectDeleted()
    {
        _cell.Set("key", "value", StorageLayer.Session);
        var snapshot = _manager.CreateSnapshot(_cell);

        _cell.Remove("key", StorageLayer.Session);

        _manager.RestoreSnapshot(_cell, snapshot);

        Assert.IsFalse(_cell.Contains("key", StorageLayer.Session));
    }

    [TestMethod]
    public void CalculateDiff_DetectsAdded()
    {
        var before = _manager.CreateSnapshot(_cell);
        _cell.Set("new-key", "new-value", StorageLayer.Session);
        var after = _manager.CreateSnapshot(_cell);

        var diffs = _manager.CalculateDiff(before, after);

        Assert.AreEqual(1, diffs.Count);
        Assert.AreEqual("new-key", diffs[0].Key);
        Assert.AreEqual(DiffType.Added, diffs[0].Type);
    }

    [TestMethod]
    public void CalculateDiff_DetectsRemoved()
    {
        _cell.Set("key", "value", StorageLayer.Session);
        var before = _manager.CreateSnapshot(_cell);
        _cell.Remove("key", StorageLayer.Session);
        var after = _manager.CreateSnapshot(_cell);

        var diffs = _manager.CalculateDiff(before, after);

        Assert.AreEqual(1, diffs.Count);
        Assert.AreEqual("key", diffs[0].Key);
        Assert.AreEqual(DiffType.Removed, diffs[0].Type);
    }

    [TestMethod]
    public void CalculateDiff_DetectsModified()
    {
        _cell.Set("key", "old-value", StorageLayer.Session);
        var before = _manager.CreateSnapshot(_cell);
        _cell.Set("key", "new-value", StorageLayer.Session);
        var after = _manager.CreateSnapshot(_cell);

        var diffs = _manager.CalculateDiff(before, after);

        Assert.AreEqual(1, diffs.Count);
        Assert.AreEqual("key", diffs[0].Key);
        Assert.AreEqual(DiffType.Modified, diffs[0].Type);
        Assert.AreEqual("old-value", diffs[0].OldValue);
        Assert.AreEqual("new-value", diffs[0].NewValue);
    }

    [TestMethod]
    public void CalculateDiff_NoChanges_ReturnsEmpty()
    {
        var before = _manager.CreateSnapshot(_cell);
        var after = _manager.CreateSnapshot(_cell);

        var diffs = _manager.CalculateDiff(before, after);

        Assert.AreEqual(0, diffs.Count);
    }

    [TestMethod]
    public void CalculateDiff_MultipleChanges()
    {
        _cell.Set("a", 1, StorageLayer.Session);
        _cell.Set("b", 2, StorageLayer.Session);
        var before = _manager.CreateSnapshot(_cell);

        _cell.Set("a", 10, StorageLayer.Session);
        _cell.Remove("b", StorageLayer.Session);
        _cell.Set("c", 3, StorageLayer.Session);
        var after = _manager.CreateSnapshot(_cell);

        var diffs = _manager.CalculateDiff(before, after);

        Assert.AreEqual(3, diffs.Count);
    }

    [TestMethod]
    public void CreateSnapshot_ReturnsSnapshotWithId()
    {
        var snapshot = _manager.CreateSnapshot(_cell);

        Assert.IsNotNull(snapshot.Id);
        Assert.AreNotEqual(string.Empty, snapshot.Id);
    }

    [TestMethod]
    public void CreateSnapshot_ReturnsSnapshotWithTimestamp()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var snapshot = _manager.CreateSnapshot(_cell);
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.IsTrue(snapshot.CreatedAt >= before);
        Assert.IsTrue(snapshot.CreatedAt <= after);
    }
}
