
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.DiagnosticContext.Core;
using Modules.DiagnosticContext.Formatting;

namespace DiagnosticContext.Tests.Formatting;

[TestClass]
public class JsonFormatterTests
{
    [TestMethod]
    public void Format_空节点_应输出JSON()
    {
        var node = new ScopeNode("empty");
        var result = JsonFormatter.Default.Format(node);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Contains("\"name\""));
        Assert.IsTrue(result.Contains("empty"));
    }

    [TestMethod]
    public void Format_有事件_应包含事件数组()
    {
        var node = new ScopeNode("test");
        node.Record("my.event", new { Value = 42 });

        var result = JsonFormatter.Default.Format(node);

        Assert.IsTrue(result.Contains("\"events\""));
        Assert.IsTrue(result.Contains("my.event"));
    }

    [TestMethod]
    public void Format_有状态_应包含状态对象()
    {
        var node = new ScopeNode("test");
        node.SetState("key1", "value1");

        var result = JsonFormatter.Default.Format(node);

        Assert.IsTrue(result.Contains("\"state\""));
        Assert.IsTrue(result.Contains("key1"));
        Assert.IsTrue(result.Contains("value1"));
    }

    [TestMethod]
    public void Format_有子节点_应包含children数组()
    {
        var root = new ScopeNode("root");
        root.CreateScope("child");

        var result = JsonFormatter.Default.Format(root);

        Assert.IsTrue(result.Contains("\"children\""));
        Assert.IsTrue(result.Contains("child"));
    }

    [TestMethod]
    public void Format_多层嵌套_应递归输出()
    {
        var root = new ScopeNode("root");
        var child = root.CreateScope("child");
        child.CreateScope("grandchild");

        var result = JsonFormatter.Default.Format(root);

        Assert.IsTrue(result.Contains("root"));
        Assert.IsTrue(result.Contains("child"));
        Assert.IsTrue(result.Contains("grandchild"));
    }

    [TestMethod]
    public void Format_输出应为合法JSON()
    {
        var node = new ScopeNode("test");
        node.Record("event1");
        node.SetState("key", "value");

        var result = JsonFormatter.Default.Format(node);

        var doc = System.Text.Json.JsonDocument.Parse(result);
        Assert.IsNotNull(doc);
        Assert.AreEqual("test", doc.RootElement.GetProperty("name").GetString());
    }
}
