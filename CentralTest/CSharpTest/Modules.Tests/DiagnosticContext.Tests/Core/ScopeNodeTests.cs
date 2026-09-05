
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.DiagnosticContext.Core;

namespace DiagnosticContext.Tests.Core;

[TestClass]
public class ScopeNodeTests
{
    [TestMethod]
    public void 创建根节点_应设置名称和创建时间()
    {
        var node = new ScopeNode("root");

        Assert.AreEqual("root", node.Name);
        Assert.IsTrue(node.CreatedAt > DateTime.MinValue);
        Assert.IsNull(node.Parent);
        Assert.AreEqual(0, node.Events.Count);
        Assert.AreEqual(0, node.State.Count);
        Assert.AreEqual(0, node.Children.Count);
    }

    [TestMethod]
    public void Record_应添加事件()
    {
        var node = new ScopeNode("root");
        node.Record("test.event", new { Value = 1 });

        Assert.AreEqual(1, node.Events.Count);
        Assert.AreEqual("test.event", node.Events[0].EventName);
    }

    [TestMethod]
    public void Record_多次调用_应添加多个事件()
    {
        var node = new ScopeNode("root");
        node.Record("event1");
        node.Record("event2");
        node.Record("event3");

        Assert.AreEqual(3, node.Events.Count);
    }

    [TestMethod]
    public void SetState_应存储状态()
    {
        var node = new ScopeNode("root");
        node.SetState("key1", "value1");

        Assert.AreEqual("value1", node.State["key1"]);
    }

    [TestMethod]
    public void SetState_同名覆盖_应更新值()
    {
        var node = new ScopeNode("root");
        node.SetState("key", "old");
        node.SetState("key", "new");

        Assert.AreEqual("new", node.State["key"]);
    }

    [TestMethod]
    public void GetState_应返回正确类型()
    {
        var node = new ScopeNode("root");
        node.SetState("count", 42);

        Assert.AreEqual(42, node.GetState<int>("count"));
    }

    [TestMethod]
    public void GetState_键不存在_应返回默认值()
    {
        var node = new ScopeNode("root");

        Assert.AreEqual(0, node.GetState<int>("nonexistent"));
        Assert.IsNull(node.GetState<string>("nonexistent"));
    }

    [TestMethod]
    public void CreateScope_应创建子节点()
    {
        var parent = new ScopeNode("parent");
        var child = parent.CreateScope("child");

        Assert.AreEqual(1, parent.Children.Count);
        Assert.AreEqual("child", child.Name);
        Assert.AreSame(parent, child.Parent);
    }

    [TestMethod]
    public void CreateScope_多层嵌套_应形成树形结构()
    {
        var root = new ScopeNode("root");
        var level1 = root.CreateScope("level1");
        var level2 = level1.CreateScope("level2");

        Assert.AreEqual(1, root.Children.Count);
        Assert.AreEqual(1, level1.Children.Count);
        Assert.AreEqual("level2", level2.Name);
        Assert.AreSame(root, level2.Root);
    }

    [TestMethod]
    public void Root_子节点_应返回根节点()
    {
        var root = new ScopeNode("root");
        var child = root.CreateScope("child");
        var grandchild = child.CreateScope("grandchild");

        Assert.AreSame(root, child.Root);
        Assert.AreSame(root, grandchild.Root);
    }

    [TestMethod]
    public void Dispose_后Record_应静默忽略()
    {
        var node = new ScopeNode("root");
        node.Dispose();
        node.Record("after.dispose");

        Assert.AreEqual(0, node.Events.Count);
    }

    [TestMethod]
    public void Dispose_后SetState_应静默忽略()
    {
        var node = new ScopeNode("root");
        node.Dispose();
        node.SetState("key", "value");

        Assert.AreEqual(0, node.State.Count);
    }

    [TestMethod]
    public void Dispose_后CreateScope_应抛出ObjectDisposedException()
    {
        var node = new ScopeNode("root");
        node.Dispose();

        Assert.ThrowsException<ObjectDisposedException>(() => node.CreateScope("child"));
    }

    [TestMethod]
    public void Dispose_后数据仍可读取()
    {
        var node = new ScopeNode("root");
        node.Record("event1");
        node.SetState("key", "value");
        node.Dispose();

        Assert.AreEqual(1, node.Events.Count);
        Assert.AreEqual("value", node.State["key"]);
    }

    [TestMethod]
    public void ToString_应输出文本格式()
    {
        var node = new ScopeNode("test");
        node.Record("my.event");

        var text = node.ToString();
        Assert.IsNotNull(text);
        Assert.IsTrue(text.Contains("test"));
        Assert.IsTrue(text.Contains("my.event"));
    }

    [TestMethod]
    public void ToSummary_应输出摘要格式()
    {
        var node = new ScopeNode("test");
        node.Record("event1");

        var summary = node.ToSummary();
        Assert.IsNotNull(summary);
        Assert.IsTrue(summary.Contains("Summary"));
        Assert.IsTrue(summary.Contains("test"));
    }

    [TestMethod]
    public void ToJson_应输出JSON格式()
    {
        var node = new ScopeNode("test");
        node.Record("event1");

        var json = node.ToJson();
        Assert.IsNotNull(json);
        Assert.IsTrue(json.Contains("\"name\""));
        Assert.IsTrue(json.Contains("test"));
    }

    [TestMethod]
    public void 名称null_应抛出ArgumentNullException()
    {
        Assert.ThrowsException<ArgumentNullException>(() => new ScopeNode(null!));
    }

    [TestMethod]
    public void 多次Dispose_应安全()
    {
        var node = new ScopeNode("root");
        node.Dispose();
        node.Dispose();
    }
}
