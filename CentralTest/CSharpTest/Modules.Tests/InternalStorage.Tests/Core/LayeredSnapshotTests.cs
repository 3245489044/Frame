
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using TConfig.ContextFrame.Abstractions;
using TestInfrastructure;

namespace InternalStorage.Tests.Core;

[TestClass]
public class LayeredSnapshotTests : TestBase
{
    [TestMethod]
    public void Constructor_SetsProperties()
    {
        var id = "snapshot-001";
        var data = new Dictionary<string, object?>
        {
            ["key1"] = "value1",
            ["key2"] = 42
        };

        var snapshot = new LayeredSnapshot(id, data);

        Assert.AreEqual(id, snapshot.Id);
        Assert.AreEqual(2, snapshot.SessionData.Count);
        Assert.AreEqual("value1", snapshot.SessionData["key1"]);
        Assert.AreEqual(42, snapshot.SessionData["key2"]);
    }

    [TestMethod]
    public void Constructor_SetsCreatedAt()
    {
        var before = DateTime.UtcNow.AddSeconds(-1);
        var snapshot = new LayeredSnapshot("id",
            new Dictionary<string, object?>());
        var after = DateTime.UtcNow.AddSeconds(1);

        Assert.IsTrue(snapshot.CreatedAt >= before);
        Assert.IsTrue(snapshot.CreatedAt <= after);
    }

    [TestMethod]
    public void SessionData_IsImmutable()
    {
        var data = new Dictionary<string, object?>
        {
            ["key"] = "value"
        };
        var snapshot = new LayeredSnapshot("id", data);

        data["key"] = "modified";

        Assert.AreEqual("value", snapshot.SessionData["key"]);
    }

    [TestMethod]
    public void ToContextSnapshot_ReturnsAdapter()
    {
        var data = new Dictionary<string, object?>
        {
            ["key"] = "value"
        };
        var snapshot = new LayeredSnapshot("snap-1", data);

        var contextSnapshot = snapshot.ToContextSnapshot();

        Assert.IsNotNull(contextSnapshot);
        Assert.AreEqual("value", contextSnapshot.Data["key"]);
    }

    [TestMethod]
    public void ToContextSnapshot_HasTimestamp()
    {
        var snapshot = new LayeredSnapshot("id",
            new Dictionary<string, object?>());

        var contextSnapshot = snapshot.ToContextSnapshot();

        Assert.IsNotNull(contextSnapshot.Data);
    }

    [TestMethod]
    public void MultipleSnapshots_HaveDifferentIds()
    {
        var snap1 = new LayeredSnapshot("id-1",
            new Dictionary<string, object?>());
        var snap2 = new LayeredSnapshot("id-2",
            new Dictionary<string, object?>());

        Assert.AreNotEqual(snap1.Id, snap2.Id);
    }
}
