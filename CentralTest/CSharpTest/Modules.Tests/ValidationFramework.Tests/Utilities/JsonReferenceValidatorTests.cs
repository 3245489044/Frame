
using System.Text.Json;
using ValidationFramework.Abstractions;
using ValidationFramework.Utilities;

namespace ValidationFramework.Tests.Utilities;

[TestClass]
public class JsonReferenceValidatorTests
{
    [TestMethod]
    public void FindStringPropertyReferences_应收集字符串引用()
    {
        const string json = """
        {
          "steps": [
            { "FunctionName": "f1" },
            { "FunctionName": "f2" }
          ]
        }
        """;
        using var doc = JsonDocument.Parse(json);

        var refs = JsonReferenceValidator.FindStringPropertyReferences(doc.RootElement, "FunctionName");

        Assert.AreEqual(2, refs.Count);
        Assert.AreEqual("f1", refs[0].Name);
        Assert.AreEqual("$.steps[0].FunctionName", refs[0].Path);
        Assert.AreEqual("f2", refs[1].Name);
    }

    [TestMethod]
    public void FindStringPropertyReferences_非字符串值_应跳过()
    {
        const string json = """{ "items": { "FunctionName": 42 } }""";
        using var doc = JsonDocument.Parse(json);

        var refs = JsonReferenceValidator.FindStringPropertyReferences(doc.RootElement, "FunctionName");

        Assert.AreEqual(0, refs.Count);
    }

    [TestMethod]
    public void FindStringPropertyReferences_空字符串_应跳过()
    {
        const string json = """{ "items": { "FunctionName": "" } }""";
        using var doc = JsonDocument.Parse(json);

        var refs = JsonReferenceValidator.FindStringPropertyReferences(doc.RootElement, "FunctionName");

        Assert.AreEqual(0, refs.Count);
    }

    [TestMethod]
    public void FindStringReferencesInContainerArrays_应收集数组元素()
    {
        const string json = """
        {
          "events": {
            "click": ["f1", "f2"],
            "hover": "notArray"
          }
        }
        """;
        using var doc = JsonDocument.Parse(json);

        var refs = JsonReferenceValidator.FindStringReferencesInContainerArrays(doc.RootElement, "events");

        Assert.AreEqual(2, refs.Count);
        Assert.AreEqual("f1", refs[0].Name);
        Assert.AreEqual("$.events.click[0]", refs[0].Path);
        Assert.AreEqual("$.events.click[1]", refs[1].Path);
    }

    [TestMethod]
    public void FindStringReferencesInContainerArrays_容器非对象_应跳过()
    {
        const string json = """{ "events": ["f1"] }""";
        using var doc = JsonDocument.Parse(json);

        var refs = JsonReferenceValidator.FindStringReferencesInContainerArrays(doc.RootElement, "events");

        Assert.AreEqual(0, refs.Count);
    }

    [TestMethod]
    public void CollectSourceReferences_应收集source引用()
    {
        const string json = """
        {
          "bindings": {
            "b1": { "source": "objA" }
          }
        }
        """;
        using var doc = JsonDocument.Parse(json);
        var bindings = doc.RootElement.GetProperty("bindings");

        var refs = JsonReferenceValidator.CollectSourceReferences(bindings, "$.bindings");

        Assert.AreEqual(1, refs.Count);
        Assert.AreEqual("objA", refs[0].Name);
        Assert.AreEqual("$.bindings.b1.source", refs[0].Path);
    }

    [TestMethod]
    public void IsPathSegmentPrefix_路径段前缀_应匹配()
    {
        Assert.IsTrue(JsonReferenceValidator.IsPathSegmentPrefix("$.a.b", "$.a.b.c"));
        Assert.IsTrue(JsonReferenceValidator.IsPathSegmentPrefix("$.a", "$.a[0]"));

        Assert.IsFalse(JsonReferenceValidator.IsPathSegmentPrefix("$.a.b", "$.a.bcX"));
        Assert.IsFalse(JsonReferenceValidator.IsPathSegmentPrefix("$.a.b.c", "$.a.b"));
        Assert.IsFalse(JsonReferenceValidator.IsPathSegmentPrefix("$.a", "$.ab"));
    }

    [TestMethod]
    public void IsPathSegmentPrefix_根路径_应匹配所有()
    {
        Assert.IsTrue(JsonReferenceValidator.IsPathSegmentPrefix("$", "$.anything"));
        Assert.IsTrue(JsonReferenceValidator.IsPathSegmentPrefix("$", "$"));
    }

    [TestMethod]
    public void ExtractScopeFromPath_空配置_应返回null()
    {
        using var doc = JsonDocument.Parse("""{}""");

        var scope = JsonReferenceValidator.ExtractScopeFromPath(doc.RootElement, "$.bindings");

        Assert.IsNull(scope);
    }

    [TestMethod]
    public void ExtractScopeFromPath_无匹配作用域_应返回null()
    {
        const string json = """
        {
          "other": { "scope": "other-scope" },
          "bindings": { "b1": { "source": "obj" } }
        }
        """;
        using var doc = JsonDocument.Parse(json);

        var scope = JsonReferenceValidator.ExtractScopeFromPath(doc.RootElement, "$.bindings");

        Assert.IsNull(scope);
    }

    [TestMethod]
    public void ValidateExistence_未注册_应生成错误诊断()
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var refs = new[] { ("Missing", "$.x") };

        JsonReferenceValidator.ValidateExistence(
            diagnostics, "RuleA", "file.json", refs,
            name => name == "Registered",
            () => new[] { "Registered" },
            "函数");

        Assert.AreEqual(1, diagnostics.Count);
        Assert.AreEqual(Severity.Error, diagnostics[0].Severity);
        Assert.IsTrue(diagnostics[0].Message.Contains("'Missing' 未注册"));
        Assert.IsTrue(diagnostics[0].RepairSuggestion!.Contains("Registered"));
        Assert.AreEqual("$.x", diagnostics[0].SourceLocation?.JsonPath);
    }

    [TestMethod]
    public void ValidateExistence_已注册_应无诊断()
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var refs = new[] { ("Registered", "$.x") };

        JsonReferenceValidator.ValidateExistence(
            diagnostics, "RuleA", "file.json", refs,
            name => name == "Registered",
            () => new[] { "Registered" },
            "函数");

        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public void ValidateExistence_空白引用_应跳过()
    {
        var diagnostics = new List<ValidationDiagnostic>();
        var refs = new[] { (" ", "$.x") };

        JsonReferenceValidator.ValidateExistence(
            diagnostics, "RuleA", "file.json", refs,
            _ => false,
            () => Array.Empty<string>(),
            "函数");

        Assert.AreEqual(0, diagnostics.Count);
    }

    [TestMethod]
    public void FormatAvailableItems_应截断到maxItems()
    {
        var items = Enumerable.Range(0, 20).Select(i => $"item{i}");

        var result = JsonReferenceValidator.FormatAvailableItems(items, maxItems: 3);

        Assert.AreEqual("item0, item1, item2", result);
    }
}