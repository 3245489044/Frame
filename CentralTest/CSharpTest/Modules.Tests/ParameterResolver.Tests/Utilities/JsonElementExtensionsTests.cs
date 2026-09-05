
using System.Text.Json;

namespace ParameterResolver.Tests.Utilities;

[TestClass]
public class JsonElementExtensionsTests
{
    [TestMethod]
    public void TryGetProperty_ExistingStringProperty_ShouldReturnValue()
    {
        var json = """{"name":"test"}""";
        var element = JsonDocument.Parse(json).RootElement;

        var result = element.TryGetProperty("name", out var value);

        Assert.IsTrue(result);
        Assert.AreEqual("test", value.GetString());
    }

    [TestMethod]
    public void TryGetProperty_MissingProperty_ShouldReturnFalse()
    {
        var json = """{"name":"test"}""";
        var element = JsonDocument.Parse(json).RootElement;

        var result = element.TryGetProperty("nonExistent", out _);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void TryGetProperty_NullValue_ShouldReturnTrueWithNull()
    {
        var json = """{"name":null}""";
        var element = JsonDocument.Parse(json).RootElement;

        var result = element.TryGetProperty("name", out var value);

        Assert.IsTrue(result);
        Assert.AreEqual(JsonValueKind.Null, value.ValueKind);
    }

    [TestMethod]
    public void TryGetProperty_NonStringValue_ShouldReturnValue()
    {
        var json = """{"count":42}""";
        var element = JsonDocument.Parse(json).RootElement;

        var result = element.TryGetProperty("count", out var value);

        Assert.IsTrue(result);
        Assert.AreEqual(42, value.GetInt32());
    }

    [TestMethod]
    public void TryGetProperty_ExistingArray_ShouldReturnElements()
    {
        var json = """{"items":["a","b","c"]}""";
        var element = JsonDocument.Parse(json).RootElement;

        var result = element.TryGetProperty("items", out var value);

        Assert.IsTrue(result);
        Assert.AreEqual(JsonValueKind.Array, value.ValueKind);
        Assert.AreEqual(3, value.GetArrayLength());
    }

    [TestMethod]
    public void TryGetProperty_MissingArrayProperty_ShouldReturnFalse()
    {
        var json = """{"items":["a"]}""";
        var element = JsonDocument.Parse(json).RootElement;

        var result = element.TryGetProperty("nonExistent", out _);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void TryGetProperty_ExistingObject_ShouldReturnElement()
    {
        var json = """{"config":{"key":"value"}}""";
        var element = JsonDocument.Parse(json).RootElement;

        var result = element.TryGetProperty("config", out var value);

        Assert.IsTrue(result);
        Assert.AreEqual(JsonValueKind.Object, value.ValueKind);
    }

    [TestMethod]
    public void TryGetProperty_MissingObjectProperty_ShouldReturnFalse()
    {
        var json = """{"config":{"key":"value"}}""";
        var element = JsonDocument.Parse(json).RootElement;

        var result = element.TryGetProperty("nonExistent", out _);

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void HasProperty_Existing_ShouldReturnTrue()
    {
        var json = """{"name":"test"}""";
        var element = JsonDocument.Parse(json).RootElement;

        Assert.IsTrue(element.TryGetProperty("name", out _));
    }

    [TestMethod]
    public void HasProperty_Missing_ShouldReturnFalse()
    {
        var json = """{"name":"test"}""";
        var element = JsonDocument.Parse(json).RootElement;

        Assert.IsFalse(element.TryGetProperty("nonExistent", out _));
    }
}
