
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using Modules.InternalStorage.Extensions;
using TConfig.ContextFrame.Abstractions;
using TestInfrastructure;

namespace InternalStorage.Tests.Extensions;

[TestClass]
public class StorageBidirectionalMapTests : TestBase
{
    private IStorageCell _cell = null!;

    [TestInitialize]
    public override void TestInitialize()
    {
        base.TestInitialize();
        _cell = new StorageCell("test-cell");
    }


    [TestMethod]
    public void MapForward_GetForward_ReturnsValue()
    {
        const string mapName = "entity-control";

        _cell.MapForward<string, string>(mapName, "entity-1", "control-A");

        var actual = _cell.GetForward<string, string>(mapName, "entity-1");
        Assert.AreEqual("control-A", actual);
    }

    [TestMethod]
    public void MapForward_Overwrite_UpdatesValue()
    {
        const string mapName = "entity-control";
        _cell.MapForward<string, string>(mapName, "entity-1", "control-A");

        _cell.MapForward<string, string>(mapName, "entity-1", "control-B");

        var actual = _cell.GetForward<string, string>(mapName, "entity-1");
        Assert.AreEqual("control-B", actual);
    }

    [TestMethod]
    public void GetForward_NonExistent_ReturnsDefault()
    {
        const string mapName = "entity-control";

        var actual = _cell.GetForward<string, string>(mapName, "nonexistent");

        Assert.IsNull(actual);
    }


    [TestMethod]
    public void MapForward_GetBackward_ReturnsKey()
    {
        const string mapName = "entity-control";
        _cell.MapForward<string, string>(mapName, "entity-1", "control-A");

        var actual = _cell.GetBackward<string, string>(mapName, "control-A");

        Assert.AreEqual("entity-1", actual);
    }

    [TestMethod]
    public void GetBackward_NonExistent_ReturnsDefault()
    {
        const string mapName = "entity-control";

        var actual = _cell.GetBackward<string, string>(mapName, "nonexistent");

        Assert.IsNull(actual);
    }


    [TestMethod]
    public void Unmap_RemovesBothDirections()
    {
        const string mapName = "entity-control";
        _cell.MapForward<string, string>(mapName, "entity-1", "control-A");

        _cell.Unmap<string, string>(mapName, "entity-1");

        Assert.IsNull(_cell.GetForward<string, string>(mapName, "entity-1"));
        Assert.IsNull(_cell.GetBackward<string, string>(mapName, "control-A"));
    }

    [TestMethod]
    public void Unmap_NonExistent_DoesNotThrow()
    {
        const string mapName = "entity-control";

        _cell.Unmap<string, string>(mapName, "nonexistent");
    }


    [TestMethod]
    public void ContainsForward_ReturnsTrue_WhenExists()
    {
        const string mapName = "entity-control";
        _cell.MapForward<string, string>(mapName, "entity-1", "control-A");

        Assert.IsTrue(_cell.ContainsForward<string, string>(mapName, "entity-1"));
    }

    [TestMethod]
    public void ContainsForward_ReturnsFalse_WhenNotExists()
    {
        const string mapName = "entity-control";

        Assert.IsFalse(_cell.ContainsForward<string, string>(mapName, "nonexistent"));
    }

    [TestMethod]
    public void ContainsBackward_ReturnsTrue_WhenExists()
    {
        const string mapName = "entity-control";
        _cell.MapForward<string, string>(mapName, "entity-1", "control-A");

        Assert.IsTrue(_cell.ContainsBackward<string, string>(mapName, "control-A"));
    }

    [TestMethod]
    public void ContainsBackward_ReturnsFalse_WhenNotExists()
    {
        const string mapName = "entity-control";

        Assert.IsFalse(_cell.ContainsBackward<string, string>(mapName, "nonexistent"));
    }


    [TestMethod]
    public void MultipleMaps_AreIsolated()
    {
        const string map1 = "entity-control";
        const string map2 = "user-role";
        _cell.MapForward<string, string>(map1, "entity-1", "control-A");
        _cell.MapForward<string, string>(map2, "user-1", "admin");

        Assert.AreEqual("control-A", _cell.GetForward<string, string>(map1, "entity-1"));
        Assert.AreEqual("admin", _cell.GetForward<string, string>(map2, "user-1"));

        Assert.AreEqual("entity-1", _cell.GetBackward<string, string>(map1, "control-A"));
        Assert.AreEqual("user-1", _cell.GetBackward<string, string>(map2, "admin"));
    }


    [TestMethod]
    public void MapForward_IntKey_WorksCorrectly()
    {
        const string mapName = "id-mapping";

        _cell.MapForward<int, string>(mapName, 42, "value-42");

        Assert.AreEqual("value-42", _cell.GetForward<int, string>(mapName, 42));
        Assert.AreEqual(42, _cell.GetBackward<int, string>(mapName, "value-42"));
    }

    [TestMethod]
    public void MapForward_IntValue_WorksCorrectly()
    {
        const string mapName = "name-to-id";

        _cell.MapForward<string, int>(mapName, "item", 100);

        Assert.AreEqual(100, _cell.GetForward<string, int>(mapName, "item"));
        Assert.AreEqual("item", _cell.GetBackward<string, int>(mapName, 100));
    }
}
