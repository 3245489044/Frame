
using System.Text.Json;
using ParameterResolver.Utilities;

namespace ParameterResolver.Tests.Utilities;

[TestClass]
public class JsonPathResolverTests
{
    private static JsonElement CreateTestElement(string json)
        => JsonDocument.Parse(json).RootElement;

    [TestMethod]
    public void Resolve_SimplePath_ShouldReturnCorrectSegments()
    {
        var root = CreateTestElement("""{"controls":[{"name":"btn1"}]}""");
        var result = JsonPathResolver.Resolve(root, "controls[]");

        Assert.IsNotNull(result);
        Assert.AreEqual(1, result.Count());
    }

    [TestMethod]
    public void Resolve_RootPath_ShouldReturnRoot()
    {
        var root = CreateTestElement("""{"key":"value"}""");
        var result = JsonPathResolver.Resolve(root, "");

        Assert.IsNotNull(result);
    }

    [TestMethod]
    public void Resolve_NestedPath_ShouldReturnAllSegments()
    {
        var root = CreateTestElement("""
        {
            "ui": {
                "views": [{
                    "controls": [{
                        "name": "btn1",
                        "events": ["Click"]
                    }]
                }]
            }
        }
        """);

        var result = JsonPathResolver.Resolve(root, "ui.views[].controls[]");

        Assert.AreEqual(1, result.Count());
    }

    [TestMethod]
    public void Resolve_WithArrayTraversal_ShouldReturnMultiple()
    {
        var root = CreateTestElement("""
        {
            "controls": [
                { "name": "btn1" },
                { "name": "btn2" }
            ]
        }
        """);

        var result = JsonPathResolver.Resolve(root, "controls[]");

        Assert.AreEqual(2, result.Count());
    }

    [TestMethod]
    public void Resolve_NonExistentPath_ShouldReturnEmpty()
    {
        var root = CreateTestElement("""{"key":"value"}""");
        var result = JsonPathResolver.Resolve(root, "nonexistent");

        Assert.AreEqual(0, result.Count());
    }

    [TestMethod]
    public void ResolveStrings_ShouldReturnStringValues()
    {
        var root = CreateTestElement("""
        {
            "controls": [
                { "name": "btn1" },
                { "name": "btn2" }
            ]
        }
        """);

        var result = JsonPathResolver.ResolveStrings(root, "controls[].name");

        Assert.AreEqual(2, result.Count());
        Assert.IsTrue(result.Contains("btn1"));
        Assert.IsTrue(result.Contains("btn2"));
    }

    [TestMethod]
    public void ResolveFirst_ShouldReturnFirstMatch()
    {
        var root = CreateTestElement("""
        {
            "controls": [
                { "name": "btn1" },
                { "name": "btn2" }
            ]
        }
        """);

        var result = JsonPathResolver.ResolveFirst(root, "controls[].name");

        Assert.IsNotNull(result);
        Assert.AreEqual("btn1", result.Value.GetString());
    }

    [TestMethod]
    public void ResolveFirst_NonExistent_ShouldReturnNull()
    {
        var root = CreateTestElement("""{"key":"value"}""");
        var result = JsonPathResolver.ResolveFirst(root, "nonexistent");

        Assert.IsTrue(result.HasValue);
        Assert.AreEqual(JsonValueKind.Undefined, result.Value.ValueKind);
    }
}
