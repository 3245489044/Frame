
using System.Text.Json;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.InternalStorage.Core;
using TConfig.ContextFrame.Abstractions;
using TestInfrastructure;

namespace InternalStorage.Tests.Core;

[TestClass]
public class StorageRegistryTests : TestBase
{
    private StorageRegistry _registry = null!;

    [TestInitialize]
    public override void TestInitialize()
    {
        base.TestInitialize();
        _registry = new StorageRegistry();
    }

    [TestMethod]
    public void Register_Resolve_SameType()
    {
        var expected = "hello-world";

        _registry.Register("greeting", expected);
        var actual = _registry.Resolve<string>("greeting");

        Assert.AreEqual(expected, actual);
    }

    [TestMethod]
    public void Register_Resolve_IntValue()
    {
        _registry.Register("count", 42);

        var actual = _registry.Resolve<int>("count");

        Assert.AreEqual(42, actual);
    }

    [TestMethod]
    public void Register_Resolve_WrongType_ReturnsDefault()
    {
        _registry.Register("key", "string-value");

        var actual = _registry.Resolve<int>("key");

        Assert.AreEqual(0, actual);
    }

    [TestMethod]
    public void TryResolve_ReturnsTrue_WhenExists()
    {
        _registry.Register("key", "value");

        var result = _registry.TryResolve<string>("key", out var value);

        Assert.IsTrue(result);
        Assert.AreEqual("value", value);
    }

    [TestMethod]
    public void TryResolve_ReturnsFalse_WhenNotExists()
    {
        var result = _registry.TryResolve<string>("nonexistent", out var value);

        Assert.IsFalse(result);
        Assert.IsNull(value);
    }

    [TestMethod]
    public void TryResolve_ReturnsFalse_WhenTypeMismatch()
    {
        _registry.Register("key", 123);

        var result = _registry.TryResolve<string>("key", out var value);

        Assert.IsFalse(result);
        Assert.IsNull(value);
    }

    [TestMethod]
    public void Contains_ReturnsTrue_WhenExists()
    {
        _registry.Register("key", "value");

        Assert.IsTrue(_registry.Contains("key"));
    }

    [TestMethod]
    public void Contains_ReturnsFalse_WhenNotExists()
    {
        Assert.IsFalse(_registry.Contains("nonexistent"));
    }

    [TestMethod]
    public void RegisterFromJson_Batch()
    {
        var data = new Dictionary<string, object?>
        {
            ["name"] = "test",
            ["count"] = 100,
            ["active"] = true
        };

        _registry.RegisterFromJson(data);

        Assert.IsTrue(_registry.Contains("name"));
        Assert.IsTrue(_registry.Contains("count"));
        Assert.IsTrue(_registry.Contains("active"));
        Assert.AreEqual("test", _registry.Resolve<string>("name"));
        Assert.AreEqual(100, _registry.Resolve<int>("count"));
        Assert.AreEqual(true, _registry.Resolve<bool>("active"));
    }

    [TestMethod]
    public void RegisterFromJson_NullData_DoesNotThrow()
    {
        _registry.RegisterFromJson((Dictionary<string, object?>)null!);
    }

    [TestMethod]
    public void RegisterFromJsonElement_Batch()
    {
        var json = """
        {
            "name": "test",
            "value": 42
        }
        """;
        var element = JsonDocument.Parse(json).RootElement;
        var data = new Dictionary<string, JsonElement>
        {
            ["name"] = element.GetProperty("name"),
            ["value"] = element.GetProperty("value")
        };

        _registry.RegisterFromJsonElement(data);

        Assert.IsTrue(_registry.Contains("name"));
        Assert.IsTrue(_registry.Contains("value"));
    }

    [TestMethod]
    public void RegisterFromJsonElement_NullData_DoesNotThrow()
    {
        _registry.RegisterFromJsonElement((Dictionary<string, JsonElement>)null!);
    }

    [TestMethod]
    public void RegisterFromJsonElement_T_Deserializes()
    {
        var json = """{ "name": "test", "value": 42 }""";
        var element = JsonDocument.Parse(json).RootElement;

        _registry.RegisterFromJsonElement<TestData>("test-data", element);

        var resolved = _registry.Resolve<TestData>("test-data");
        Assert.IsNotNull(resolved);
        Assert.AreEqual("test", resolved!.Name);
        Assert.AreEqual(42, resolved.Value);
    }

    [TestMethod]
    public void RegisterFromJsonElement_T_InvalidJson_Throws()
    {
        var json = """{ "name": "test" }""";
        var element = JsonDocument.Parse(json).RootElement;

        var invalidJson = """{ "items": [1, 2, 3] }""";
        var invalidElement = JsonDocument.Parse(invalidJson).RootElement;

        Assert.ThrowsException<InvalidOperationException>(() =>
            _registry.RegisterFromJsonElement<List<int>>("invalid", invalidElement));
    }

    [TestMethod]
    public void GetRegisteredTypes_ReturnsAll()
    {
        _registry.Register("str", "hello");
        _registry.Register("num", 42);
        _registry.Register("flag", true);

        var types = _registry.GetRegisteredTypes();

        Assert.AreEqual(3, types.Count);
        Assert.AreEqual(typeof(string), types["str"]);
        Assert.AreEqual(typeof(int), types["num"]);
        Assert.AreEqual(typeof(bool), types["flag"]);
    }

    [TestMethod]
    public void GetRegisteredTypes_Empty_WhenNoRegistrations()
    {
        var types = _registry.GetRegisteredTypes();

        Assert.AreEqual(0, types.Count);
    }

    [TestMethod]
    public void Register_Overwrite_ExistingKey()
    {
        _registry.Register("key", "old-value");

        _registry.Register("key", "new-value");
        var actual = _registry.Resolve<string>("key");

        Assert.AreEqual("new-value", actual);
    }

    private class TestData
    {
        public string Name { get; set; } = "";
        public int Value { get; set; }
    }
}
