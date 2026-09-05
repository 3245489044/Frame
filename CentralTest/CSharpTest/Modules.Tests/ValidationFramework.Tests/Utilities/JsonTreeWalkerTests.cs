
using System.Text.Json;
using ValidationFramework.Utilities;

namespace ValidationFramework.Tests.Utilities;

[TestClass]
public class JsonTreeWalkerTests
{
    [TestMethod]
    public void FindProperties_应递归查找所有匹配属性()
    {
        const string json = """
        {
          "a": { "target": "1" },
          "b": { "c": { "target": "2" } },
          "notTarget": "x"
        }
        """;
        using var doc = JsonDocument.Parse(json);

        var results = JsonTreeWalker.FindProperties(doc.RootElement, "target");

        Assert.AreEqual(2, results.Count);
        Assert.AreEqual("$.a.target", results[0].Path);
        Assert.AreEqual("$.b.c.target", results[1].Path);
    }

    [TestMethod]
    public void FindProperties_应生成数组路径()
    {
        const string json = """
        {
          "items": [
            { "target": "1" },
            { "target": "2" }
          ]
        }
        """;
        using var doc = JsonDocument.Parse(json);

        var results = JsonTreeWalker.FindProperties(doc.RootElement, "target");

        Assert.AreEqual("$.items[0].target", results[0].Path);
        Assert.AreEqual("$.items[1].target", results[1].Path);
    }

    [TestMethod]
    public void FindProperties_maxDepth_应限制遍历深度()
    {
        const string json = """
        {
          "l1": { "target": "1" },
          "l2": { "deep": { "target": "2" } }
        }
        """;
        using var doc = JsonDocument.Parse(json);

        var results = JsonTreeWalker.FindProperties(doc.RootElement, "target", maxDepth: 1);

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual("$.l1.target", results[0].Path);
    }

    [TestMethod]
    public void FindProperties_rootPath_应使用自定义前缀()
    {
        const string json = """{ "target": "1" }""";
        using var doc = JsonDocument.Parse(json);

        var results = JsonTreeWalker.FindProperties(doc.RootElement, "target", rootPath: "$.config");

        Assert.AreEqual("$.config.target", results[0].Path);
    }

    [TestMethod]
    public void FindProperties_无匹配_应返回空()
    {
        using var doc = JsonDocument.Parse("""{ "x": 1 }""");

        var results = JsonTreeWalker.FindProperties(doc.RootElement, "target");

        Assert.AreEqual(0, results.Count);
    }

    [TestMethod]
    public void FindProperties_属性值为数组_应返回数组元素本身()
    {
        const string json = """{ "items": [1, 2, 3] }""";
        using var doc = JsonDocument.Parse(json);

        var results = JsonTreeWalker.FindProperties(doc.RootElement, "items");

        Assert.AreEqual(1, results.Count);
        Assert.AreEqual(JsonValueKind.Array, results[0].Value.ValueKind);
    }

    [TestMethod]
    public void GetStringOrNull_字符串_应返回值()
    {
        using var doc = JsonDocument.Parse("""{ "s": "hello" }""");
        var element = doc.RootElement.GetProperty("s");

        Assert.AreEqual("hello", JsonTreeWalker.GetStringOrNull(element));
    }

    [TestMethod]
    public void GetStringOrNull_非字符串_应返回null()
    {
        using var doc = JsonDocument.Parse("""{ "n": 42 }""");
        var element = doc.RootElement.GetProperty("n");

        Assert.IsNull(JsonTreeWalker.GetStringOrNull(element));
    }
}