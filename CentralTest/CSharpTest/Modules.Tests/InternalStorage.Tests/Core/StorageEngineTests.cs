
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using TConfig.ContextFrame.Abstractions;
using TConfig;
using TestInfrastructure;

namespace InternalStorage.Tests.Core;

[TestClass]
public class StorageEngineTests : TestBase
{
    [TestMethod]
    public void Constructor_SetsRunningState()
    {
        using var engine = new StorageEngine("test-engine");

        Assert.AreEqual(StorageEngineState.Running, engine.State);
    }

    [TestMethod]
    public void Constructor_CreatesRootCell()
    {
        using var engine = new StorageEngine("test-engine");

        Assert.IsNotNull(engine.RootCell);
        StringAssert.Contains(engine.RootCell.Name, "Root");
    }

    [TestMethod]
    public void Constructor_GeneratesName_WhenNotProvided()
    {
        using var engine = new StorageEngine();

        Assert.IsNotNull(engine.Name);
        StringAssert.StartsWith(engine.Name, "StorageEngine-");
    }

    [TestMethod]
    public void CreateSession_ReturnsNewCell()
    {
        using var engine = new StorageEngine("test-engine");

        var session = engine.CreateSession("session-1");

        Assert.IsNotNull(session);
        StringAssert.Contains(session.Name, "session-1");
    }

    [TestMethod]
    public void CreateSession_SessionHasRootAsParent()
    {
        using var engine = new StorageEngine("test-engine");

        var session = engine.CreateSession("session-1");

        Assert.AreSame(engine.RootCell, session.Parent);
    }

    [TestMethod]
    public void CreateSession_Duplicate_Throws()
    {
        using var engine = new StorageEngine("test-engine");
        engine.CreateSession("session-1");

        Assert.ThrowsException<InvalidOperationException>(() =>
            engine.CreateSession("session-1"));
    }

    [TestMethod]
    public void GetSession_ReturnsExisting()
    {
        using var engine = new StorageEngine("test-engine");
        engine.CreateSession("session-1");

        var session = engine.GetSession("session-1");

        Assert.IsNotNull(session);
    }

    [TestMethod]
    public void GetSession_ReturnsNull_WhenNotExists()
    {
        using var engine = new StorageEngine("test-engine");

        var session = engine.GetSession("nonexistent");

        Assert.IsNull(session);
    }

    [TestMethod]
    public void DisposeSession_RemovesSession()
    {
        using var engine = new StorageEngine("test-engine");
        engine.CreateSession("session-1");

        engine.DisposeSession("session-1");

        Assert.IsNull(engine.GetSession("session-1"));
    }

    [TestMethod]
    public void DisposeSession_ClearsAllLayers()
    {
        using var engine = new StorageEngine("test-engine");
        var session = engine.CreateSession("session-1");
        session.Set("cfg", "config-value", StorageLayer.Config);
        session.Set("ses", "session-value", StorageLayer.Session);
        session.Set("trn", "transient-value", StorageLayer.Transient);

        engine.DisposeSession("session-1");

        Assert.IsFalse(session.Contains("cfg"));
        Assert.IsFalse(session.Contains("ses"));
        Assert.IsFalse(session.Contains("trn"));
    }

    [TestMethod]
    public void Dispose_ChangesStateToDisposed()
    {
        var engine = new StorageEngine("test-engine");

        engine.Dispose();

        Assert.AreEqual(StorageEngineState.Disposed, engine.State);
    }

    [TestMethod]
    public void Dispose_DisposesAllSessions()
    {
        var engine = new StorageEngine("test-engine");
        engine.CreateSession("session-1");
        engine.CreateSession("session-2");

        engine.Dispose();

        Assert.AreEqual(0, engine.GetActiveSessionIds().Count());
    }

    [TestMethod]
    public void Dispose_ThrowsOnUse()
    {
        var engine = new StorageEngine("test-engine");
        engine.Dispose();

        Assert.ThrowsException<ObjectDisposedException>(() =>
            engine.CreateSession("any"));
    }

    [TestMethod]
    public void Dispose_ThrowsOnGetSession()
    {
        var engine = new StorageEngine("test-engine");
        engine.Dispose();

        Assert.ThrowsException<ObjectDisposedException>(() =>
            engine.GetSession("any"));
    }

    [TestMethod]
    public void Dispose_ThrowsOnDisposeSession()
    {
        var engine = new StorageEngine("test-engine");
        engine.Dispose();

        Assert.ThrowsException<ObjectDisposedException>(() =>
            engine.DisposeSession("any"));
    }

    [TestMethod]
    public void Dispose_ThrowsOnCreateSnapshot()
    {
        var engine = new StorageEngine("test-engine");
        engine.Dispose();

        Assert.ThrowsException<ObjectDisposedException>(() =>
            engine.CreateSnapshot("any"));
    }

    [TestMethod]
    public void Dispose_ThrowsOnRestoreSnapshot()
    {
        var engine = new StorageEngine("test-engine");
        engine.Dispose();

        Assert.ThrowsException<ObjectDisposedException>(() =>
            engine.RestoreSnapshot("any", null!));
    }

    [TestMethod]
    public void Session_InheritsRootConfig()
    {
        using var engine = new StorageEngine("test-engine");
        engine.RootCell.Set("global-config", "shared-value", StorageLayer.Config);

        var session = engine.CreateSession("session-1");
        var actual = session.Get<string>("global-config");

        Assert.AreEqual("shared-value", actual);
    }

    [TestMethod]
    public void Session_CanOverrideRootConfig()
    {
        using var engine = new StorageEngine("test-engine");
        engine.RootCell.Set("key", "root-value", StorageLayer.Config);

        var session = engine.CreateSession("session-1");
        session.Set("key", "session-value", StorageLayer.Session);

        var actual = session.Get<string>("key");

        Assert.AreEqual("session-value", actual);
    }

    [TestMethod]
    public void StateChanged_FiresOnDispose()
    {
        var engine = new StorageEngine("test-engine");
        StorageEngineState? oldState = null;
        StorageEngineState? newState = null;
        engine.StateChanged += (_, e) =>
        {
            oldState = e.OldState;
            newState = e.NewState;
        };

        engine.Dispose();

        Assert.AreEqual(StorageEngineState.Running, oldState);
        Assert.AreEqual(StorageEngineState.Disposed, newState);
    }

    [TestMethod]
    public void CreateSnapshot_Throws_WhenSessionNotExists()
    {
        using var engine = new StorageEngine("test-engine");

        Assert.ThrowsException<InvalidOperationException>(() =>
            engine.CreateSnapshot("nonexistent"));
    }

    [TestMethod]
    public void RestoreSnapshot_Throws_WhenSessionNotExists()
    {
        using var engine = new StorageEngine("test-engine");

        Assert.ThrowsException<InvalidOperationException>(() =>
            engine.RestoreSnapshot("nonexistent", null!));
    }

    [TestMethod]
    public void GetActiveSessionIds_ReturnsAll()
    {
        using var engine = new StorageEngine("test-engine");
        engine.CreateSession("s1");
        engine.CreateSession("s2");

        var ids = engine.GetActiveSessionIds().ToList();

        Assert.AreEqual(2, ids.Count);
        CollectionAssert.Contains(ids, "s1");
        CollectionAssert.Contains(ids, "s2");
    }

    [TestMethod]
    public void MultipleDispose_DoesNotThrow()
    {
        var engine = new StorageEngine("test-engine");

        engine.Dispose();
        engine.Dispose();
    }
}
