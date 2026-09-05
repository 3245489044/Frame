
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using Modules.InternalStorage.Extensions;
using TConfig.ContextFrame.Abstractions;
using TestInfrastructure;

namespace InternalStorage.Tests.Extensions;

[TestClass]
public class StorageRelationExtensionsTests : TestBase
{
    private IStorageCell _cell = null!;

    [TestInitialize]
    public override void TestInitialize()
    {
        base.TestInitialize();
        _cell = new StorageCell("test-cell");
    }


    [TestMethod]
    public void AddRelation_StoresRelation()
    {
        const string relationType = "contains";

        _cell.AddRelation("parent", "child", relationType);

        Assert.IsTrue(_cell.HasRelation("parent", "child", relationType));
    }

    [TestMethod]
    public void AddRelation_MultipleTargets_StoresAll()
    {
        const string relationType = "contains";

        _cell.AddRelation("parent", "child-1", relationType);
        _cell.AddRelation("parent", "child-2", relationType);

        var related = _cell.GetRelated("parent", relationType).ToList();
        Assert.AreEqual(2, related.Count);
        CollectionAssert.Contains(related, "child-1");
        CollectionAssert.Contains(related, "child-2");
    }

    [TestMethod]
    public void RemoveRelation_RemovesRelation()
    {
        const string relationType = "contains";
        _cell.AddRelation("parent", "child", relationType);

        _cell.RemoveRelation("parent", "child", relationType);

        Assert.IsFalse(_cell.HasRelation("parent", "child", relationType));
    }

    [TestMethod]
    public void RemoveRelation_NonExistent_DoesNotThrow()
    {
        const string relationType = "contains";

        _cell.RemoveRelation("parent", "nonexistent", relationType);
    }

    [TestMethod]
    public void HasRelation_ReturnsTrue_WhenExists()
    {
        const string relationType = "contains";
        _cell.AddRelation("parent", "child", relationType);

        Assert.IsTrue(_cell.HasRelation("parent", "child", relationType));
    }

    [TestMethod]
    public void HasRelation_ReturnsFalse_WhenNotExists()
    {
        const string relationType = "contains";

        Assert.IsFalse(_cell.HasRelation("parent", "child", relationType));
    }


    [TestMethod]
    public void GetRelated_ReturnsTargets()
    {
        const string relationType = "contains";
        _cell.AddRelation("parent", "child-1", relationType);
        _cell.AddRelation("parent", "child-2", relationType);

        var related = _cell.GetRelated("parent", relationType).ToList();

        Assert.AreEqual(2, related.Count);
        CollectionAssert.Contains(related, "child-1");
        CollectionAssert.Contains(related, "child-2");
    }

    [TestMethod]
    public void GetRelated_NoRelations_ReturnsEmpty()
    {
        const string relationType = "contains";

        var related = _cell.GetRelated("parent", relationType).ToList();

        Assert.AreEqual(0, related.Count);
    }


    [TestMethod]
    public void GetRelatedTo_ReturnsSources()
    {
        const string relationType = "contains";
        _cell.AddRelation("parent-1", "child", relationType);
        _cell.AddRelation("parent-2", "child", relationType);

        var sources = _cell.GetRelatedTo("child", relationType).ToList();

        Assert.AreEqual(2, sources.Count);
        CollectionAssert.Contains(sources, "parent-1");
        CollectionAssert.Contains(sources, "parent-2");
    }

    [TestMethod]
    public void GetRelatedTo_NoRelations_ReturnsEmpty()
    {
        const string relationType = "contains";

        var sources = _cell.GetRelatedTo("child", relationType).ToList();

        Assert.AreEqual(0, sources.Count);
    }


    [TestMethod]
    public void GetAllRelations_ReturnsGrouped()
    {
        _cell.AddRelation("entity", "target-1", "contains");
        _cell.AddRelation("entity", "target-2", "contains");
        _cell.AddRelation("entity", "output-1", "event_output");

        var all = _cell.GetAllRelations("entity");

        Assert.AreEqual(2, all.Count);
        Assert.IsTrue(all.ContainsKey("contains"));
        Assert.IsTrue(all.ContainsKey("event_output"));

        var containsTargets = all["contains"].ToList();
        Assert.AreEqual(2, containsTargets.Count);
        CollectionAssert.Contains(containsTargets, "target-1");
        CollectionAssert.Contains(containsTargets, "target-2");

        var eventTargets = all["event_output"].ToList();
        Assert.AreEqual(1, eventTargets.Count);
        Assert.AreEqual("output-1", eventTargets[0]);
    }

    [TestMethod]
    public void GetAllRelations_NoRelations_ReturnsEmpty()
    {
        var all = _cell.GetAllRelations("entity");

        Assert.AreEqual(0, all.Count);
    }


    [TestMethod]
    public void RemoveAllRelations_ClearsAll()
    {
        _cell.AddRelation("entity", "target-1", "contains");
        _cell.AddRelation("entity", "target-2", "contains");
        _cell.AddRelation("entity", "output-1", "event_output");

        _cell.RemoveAllRelations("entity");

        Assert.AreEqual(0, _cell.GetAllRelations("entity").Count);
        Assert.AreEqual(0, _cell.GetRelated("entity", "contains").Count());
        Assert.AreEqual(0, _cell.GetRelated("entity", "event_output").Count());

        Assert.AreEqual(0, _cell.GetRelatedTo("target-1", "contains").Count());
        Assert.AreEqual(0, _cell.GetRelatedTo("output-1", "event_output").Count());
    }

    [TestMethod]
    public void RemoveAllRelations_NoRelations_DoesNotThrow()
    {
        _cell.RemoveAllRelations("entity");
    }


    [TestMethod]
    public void MultipleRelationTypes_AreIsolated()
    {
        _cell.AddRelation("entity", "child", "contains");
        _cell.AddRelation("entity", "output", "event_output");

        var containsRelated = _cell.GetRelated("entity", "contains").ToList();
        var eventRelated = _cell.GetRelated("entity", "event_output").ToList();

        Assert.AreEqual(1, containsRelated.Count);
        Assert.AreEqual("child", containsRelated[0]);

        Assert.AreEqual(1, eventRelated.Count);
        Assert.AreEqual("output", eventRelated[0]);
    }


    [TestMethod]
    public void RemoveRelation_UpdatesReverseIndex()
    {
        _cell.AddRelation("parent", "child", "contains");

        _cell.RemoveRelation("parent", "child", "contains");

        Assert.AreEqual(0, _cell.GetRelatedTo("child", "contains").Count());
    }
}
