
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.DiagnosticContext.Core;
using Modules.DiagnosticContext.Factory;

namespace DiagnosticContext.Tests.Core;

[TestClass]
public class DiagnosticSnapshotTests
{
    [TestMethod]
    public void Freeze_应创建快照()
    {
        using var ctx = DiagnosticContextFactory.Create("test");
        ctx.Record("event1", new { Value = 42 });
        ctx.SetState("key", "value");

        var snapshot = ctx.Freeze();

        Assert.AreEqual("test", snapshot.Name);
        Assert.AreEqual(1, snapshot.Events.Count);
        Assert.AreEqual("event1", snapshot.Events[0].EventName);
        Assert.AreEqual("value", snapshot.State["key"]);
    }

    [TestMethod]
    public void Freeze_应递归快照子作用域()
    {
        using var ctx = DiagnosticContextFactory.Create("root");
        using (var child = ctx.CreateScope("child"))
        {
            child.Record("child.event");
        }

        var snapshot = ctx.Freeze();

        Assert.AreEqual(1, snapshot.Children.Count);
        Assert.AreEqual("child", snapshot.Children[0].Name);
        Assert.AreEqual(1, snapshot.Children[0].Events.Count);
        Assert.AreEqual("child.event", snapshot.Children[0].Events[0].EventName);
    }

    [TestMethod]
    public void Freeze_快照应深拷贝_修改原始节点不影响快照()
    {
        using var ctx = DiagnosticContextFactory.Create("test");
        ctx.Record("event1");
        var snapshot = ctx.Freeze();

        ctx.Record("event2");

        Assert.AreEqual(1, snapshot.Events.Count);
    }

    [TestMethod]
    public void Rehydrate_应恢复为新的上下文()
    {
        using var ctx = DiagnosticContextFactory.Create("test");
        ctx.Record("event1", new { Value = 42 });
        ctx.SetState("key", "value");

        var snapshot = ctx.Freeze();
        using var rehydrated = snapshot.Rehydrate();

        Assert.IsNotNull(rehydrated);
        Assert.AreEqual("test", rehydrated.Name);
        Assert.AreEqual(1, rehydrated.RootScope.Events.Count);
        Assert.AreEqual("event1", rehydrated.RootScope.Events[0].EventName);
        Assert.AreEqual("value", rehydrated.RootScope.State["key"]);
    }

    [TestMethod]
    public void Rehydrate_应恢复子作用域()
    {
        using var ctx = DiagnosticContextFactory.Create("root");
        using (var child = ctx.CreateScope("child"))
        {
            child.Record("child.event");
        }

        var snapshot = ctx.Freeze();
        using var rehydrated = snapshot.Rehydrate();

        Assert.AreEqual(1, rehydrated.RootScope.Children.Count);
        Assert.AreEqual("child", rehydrated.RootScope.Children[0].Name);
        Assert.AreEqual(1, rehydrated.RootScope.Children[0].Events.Count);
    }

    [TestMethod]
    public void Rehydrate_应不受原始上下文Dispose影响()
    {
        var ctx = DiagnosticContextFactory.Create("test");
        ctx.Record("event1");
        var snapshot = ctx.Freeze();
        ctx.Dispose();

        using var rehydrated = snapshot.Rehydrate();

        Assert.IsNotNull(rehydrated);
        Assert.AreEqual("test", rehydrated.Name);
        Assert.AreEqual(1, rehydrated.RootScope.Events.Count);
    }

    [TestMethod]
    public void 快照_事件列表应不可变()
    {
        using var ctx = DiagnosticContextFactory.Create("test");
        ctx.Record("event1");

        var snapshot = ctx.Freeze();

        Assert.IsInstanceOfType(snapshot.Events, typeof(IReadOnlyList<EventEntry>));
    }

    [TestMethod]
    public void 快照_状态字典应不可变()
    {
        using var ctx = DiagnosticContextFactory.Create("test");
        ctx.SetState("key", "value");

        var snapshot = ctx.Freeze();

        Assert.IsInstanceOfType(snapshot.State, typeof(IReadOnlyDictionary<string, object?>));
    }

    [TestMethod]
    public void 空上下文快照_应正确()
    {
        using var ctx = DiagnosticContextFactory.Create("empty");
        var snapshot = ctx.Freeze();

        Assert.AreEqual("empty", snapshot.Name);
        Assert.AreEqual(0, snapshot.Events.Count);
        Assert.AreEqual(0, snapshot.State.Count);
        Assert.AreEqual(0, snapshot.Children.Count);
    }
}
