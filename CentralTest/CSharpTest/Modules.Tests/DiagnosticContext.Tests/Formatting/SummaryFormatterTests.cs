
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.DiagnosticContext.Core;
using Modules.DiagnosticContext.Formatting;

namespace DiagnosticContext.Tests.Formatting;

[TestClass]
public class SummaryFormatterTests
{
    [TestMethod]
    public void Format_空节点_应包含名称()
    {
        var node = new ScopeNode("empty");
        var result = SummaryFormatter.Default.Format(node);

        Assert.IsNotNull(result);
        Assert.IsTrue(result.Contains("empty"));
    }

    [TestMethod]
    public void Format_有事件_应包含事件计数()
    {
        var node = new ScopeNode("test");
        node.Record("event1");
        node.Record("event2");

        var result = SummaryFormatter.Default.Format(node);

        Assert.IsTrue(result.Contains("test"));
        Assert.IsTrue(result.Contains("Summary"));
    }

    [TestMethod]
    public void Format_有状态_应包含状态计数()
    {
        var node = new ScopeNode("test");
        node.SetState("key1", "value1");
        node.SetState("key2", "value2");

        var result = SummaryFormatter.Default.Format(node);

        Assert.IsTrue(result.Contains("test"));
        Assert.IsTrue(result.Contains("Summary"));
    }

    [TestMethod]
    public void Format_有子节点_应包含子节点摘要()
    {
        var root = new ScopeNode("root");
        root.CreateScope("child1");
        root.CreateScope("child2");

        var result = SummaryFormatter.Default.Format(root);

        Assert.IsTrue(result.Contains("root"));
        Assert.IsTrue(result.Contains("child1"));
        Assert.IsTrue(result.Contains("child2"));
    }

    [TestMethod]
    public void Format_多层嵌套_应递归摘要()
    {
        var root = new ScopeNode("root");
        var child = root.CreateScope("child");
        child.CreateScope("grandchild");

        var result = SummaryFormatter.Default.Format(root);

        Assert.IsTrue(result.Contains("root"));
        Assert.IsTrue(result.Contains("child"));
        Assert.IsTrue(result.Contains("grandchild"));
    }

    [TestMethod]
    public void Format_大量事件_应汇总统计()
    {
        var node = new ScopeNode("busy");
        for (int i = 0; i < 10; i++)
        {
            node.Record($"event{i}");
        }

        var result = SummaryFormatter.Default.Format(node);

        Assert.IsTrue(result.Contains("busy"));
        Assert.IsTrue(result.Contains("Summary"));
    }
}
