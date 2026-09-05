
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.DiagnosticContext.Core;
using Modules.DiagnosticContext.Formatting;

namespace DiagnosticContext.Tests.Formatting;

[TestClass]
public class TextFormatterTests
{
    [TestMethod]
    public void Format_空节点_应包含名称()
    {
        var node = new ScopeNode("empty");
        var result = TextFormatter.Default.Format(node);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Contains("empty"));
    }

    [TestMethod]
    public void Format_有事件_应包含事件信息()
    {
        var node = new ScopeNode("test");
        node.Record("my.event", new { Value = 42 });

        var result = TextFormatter.Default.Format(node);

        Assert.IsTrue(result.Contains("test"));
        Assert.IsTrue(result.Contains("my.event"));
    }

    [TestMethod]
    public void Format_有状态_应包含状态信息()
    {
        var node = new ScopeNode("test");
        node.SetState("key1", "value1");

        var result = TextFormatter.Default.Format(node);

        Assert.IsTrue(result.Contains("key1"));
        Assert.IsTrue(result.Contains("value1"));
    }

    [TestMethod]
    public void Format_有子节点_应包含子节点信息()
    {
        var root = new ScopeNode("root");
        root.CreateScope("child");

        var result = TextFormatter.Default.Format(root);

        Assert.IsTrue(result.Contains("root"));
        Assert.IsTrue(result.Contains("child"));
    }

    [TestMethod]
    public void Format_多层嵌套_应缩进显示()
    {
        var root = new ScopeNode("root");
        var child = root.CreateScope("child");
        child.CreateScope("grandchild");

        var result = TextFormatter.Default.Format(root);

        Assert.IsTrue(result.Contains("root"));
        Assert.IsTrue(result.Contains("child"));
        Assert.IsTrue(result.Contains("grandchild"));
    }

    [TestMethod]
    public void Format_事件和状态混合_应全部包含()
    {
        var node = new ScopeNode("mixed");
        node.Record("event1");
        node.SetState("state1", "val1");
        node.Record("event2");

        var result = TextFormatter.Default.Format(node);

        Assert.IsTrue(result.Contains("event1"));
        Assert.IsTrue(result.Contains("event2"));
        Assert.IsTrue(result.Contains("state1"));
        Assert.IsTrue(result.Contains("val1"));
    }
}
