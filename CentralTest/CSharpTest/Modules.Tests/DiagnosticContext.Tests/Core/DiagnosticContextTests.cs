
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.DiagnosticContext.Core;
using Modules.DiagnosticContext.Factory;

namespace DiagnosticContext.Tests.Core;

[TestClass]
public class DiagnosticContextTests
{
    [TestMethod]
    public void 创建上下文_应设置名称和创建时间()
    {
        using var ctx = DiagnosticContextFactory.Create("test");

        Assert.AreEqual("test", ctx.Name);
        Assert.IsTrue(ctx.CreatedAt > DateTime.MinValue);
    }

    [TestMethod]
    public void Record_应记录事件到根作用域()
    {
        using var ctx = DiagnosticContextFactory.Create("test");
        ctx.Record("my.event", new { Value = 42 });

        Assert.AreEqual(1, ctx.RootScope.Events.Count);
        Assert.AreEqual("my.event", ctx.RootScope.Events[0].EventName);
    }

    [TestMethod]
    public void Record_多次调用_应添加多个事件()
    {
        using var ctx = DiagnosticContextFactory.Create("test");
        ctx.Record("event1");
        ctx.Record("event2");
        ctx.Record("event3");

        Assert.AreEqual(3, ctx.RootScope.Events.Count);
    }

    [TestMethod]
    public void SetState_应存储状态()
    {
        using var ctx = DiagnosticContextFactory.Create("test");
        ctx.SetState("key", "value");

        Assert.AreEqual("value", ctx.RootScope.State["key"]);
    }

    [TestMethod]
    public void GetState_应返回正确类型()
    {
        using var ctx = DiagnosticContextFactory.Create("test");
        ctx.SetState("count", 42);

        Assert.AreEqual(42, ctx.GetState<int>("count"));
    }

    [TestMethod]
    public void GetState_键不存在_应返回默认值()
    {
        using var ctx = DiagnosticContextFactory.Create("test");

        Assert.IsNull(ctx.GetState<string>("nonexistent"));
    }

    [TestMethod]
    public void CreateScope_应创建子作用域()
    {
        using var ctx = DiagnosticContextFactory.Create("test");
        using var child = ctx.CreateScope("child");

        Assert.AreEqual(1, ctx.RootScope.Children.Count);
        Assert.AreEqual("child", ctx.RootScope.Children[0].Name);
    }

    [TestMethod]
    public void CreateScope_子作用域Record_应记录到子节点()
    {
        using var ctx = DiagnosticContextFactory.Create("test");
        using (var child = ctx.CreateScope("child"))
        {
            child.Record("child.event");
        }

        Assert.AreEqual(1, ctx.RootScope.Children[0].Events.Count);
        Assert.AreEqual("child.event", ctx.RootScope.Children[0].Events[0].EventName);
    }

    [TestMethod]
    public void CreateScope_子作用域SetState_应隔离()
    {
        using var ctx = DiagnosticContextFactory.Create("test");
        ctx.SetState("parent", "value");

        using (var child = ctx.CreateScope("child"))
        {
            child.SetState("child", "childValue");
            Assert.AreEqual("value", child.GetState<string>("parent"));
        }

        Assert.IsNull(ctx.GetState<string>("child"));
    }

    [TestMethod]
    public void CreateScope_嵌套多层_应正确传播()
    {
        using var ctx = DiagnosticContextFactory.Create("root");
        ctx.Record("root.event");

        using (var level1 = ctx.CreateScope("level1"))
        {
            level1.Record("level1.event");

            using (var level2 = level1.CreateScope("level2"))
            {
                level2.Record("level2.event");
            }
        }

        Assert.AreEqual(1, ctx.RootScope.Events.Count);
        Assert.AreEqual(1, ctx.RootScope.Children[0].Events.Count);
        Assert.AreEqual(1, ctx.RootScope.Children[0].Children[0].Events.Count);
    }

    [TestMethod]
    public void Dispose_后Record_应静默忽略()
    {
        var ctx = DiagnosticContextFactory.Create("test");
        ctx.Dispose();
        ctx.Record("after.dispose");

        Assert.AreEqual(0, ctx.RootScope.Events.Count);
    }

    [TestMethod]
    public void Dispose_后CreateScope_应抛出ObjectDisposedException()
    {
        var ctx = DiagnosticContextFactory.Create("test");
        ctx.Dispose();

        Assert.ThrowsException<ObjectDisposedException>(() => ctx.CreateScope("child"));
    }

    [TestMethod]
    public void Dispose_后SetState_应静默忽略()
    {
        var ctx = DiagnosticContextFactory.Create("test");
        ctx.Dispose();
        ctx.SetState("key", "value");

        Assert.AreEqual(0, ctx.RootScope.State.Count);
    }

    [TestMethod]
    public void 多次Dispose_应安全()
    {
        var ctx = DiagnosticContextFactory.Create("test");
        ctx.Dispose();
        ctx.Dispose();
    }

    [TestMethod]
    public void Freeze_应创建快照()
    {
        using var ctx = DiagnosticContextFactory.Create("test");
        ctx.Record("event1");
        ctx.SetState("key", "value");

        var snapshot = ctx.Freeze();

        Assert.AreEqual("test", snapshot.Name);
        Assert.AreEqual(1, snapshot.Events.Count);
        Assert.AreEqual("value", snapshot.State["key"]);
    }

    [TestMethod]
    public void Freeze_快照应独立于原始上下文()
    {
        var ctx = DiagnosticContextFactory.Create("test");
        ctx.Record("event1");
        var snapshot = ctx.Freeze();

        ctx.Record("event2");

        Assert.AreEqual(1, snapshot.Events.Count);
        Assert.AreEqual(2, ctx.RootScope.Events.Count);
    }

    [TestMethod]
    public void ToString_应输出文本格式()
    {
        using var ctx = DiagnosticContextFactory.Create("test");
        ctx.Record("my.event");

        var text = ctx.ToString();
        Assert.IsNotNull(text);
        Assert.IsTrue(text.Contains("test"));
    }

    [TestMethod]
    public void ToSummary_应输出摘要格式()
    {
        using var ctx = DiagnosticContextFactory.Create("test");
        ctx.Record("event1");

        var summary = ctx.ToSummary();
        Assert.IsNotNull(summary);
        Assert.IsTrue(summary.Contains("Summary"));
    }

    [TestMethod]
    public void ToJson_应输出JSON格式()
    {
        using var ctx = DiagnosticContextFactory.Create("test");
        ctx.Record("event1");

        var json = ctx.ToJson();
        Assert.IsNotNull(json);
        Assert.IsTrue(json.Contains("\"name\""));
    }

    [TestMethod]
    public void AsyncLocal_应沿异步流传播()
    {
        string? capturedName = null;

        var task = Task.Run(() =>
        {
            using var ctx = DiagnosticManager.BeginScope("async-test");
            ctx.Record("async.event");

            var current = DiagnosticManager.Current;
            capturedName = current?.Name;
        });
        task.GetAwaiter().GetResult();

        Assert.AreEqual("async-test", capturedName);
    }

    [TestMethod]
    public void AsyncLocal_不同异步流_应隔离()
    {
        string? name1 = null;
        string? name2 = null;

        var task1 = Task.Run(() =>
        {
            using var ctx = DiagnosticManager.BeginScope("flow1");
            name1 = DiagnosticManager.Current?.Name;
            Thread.Sleep(100);
        });

        var task2 = Task.Run(() =>
        {
            using var ctx = DiagnosticManager.BeginScope("flow2");
            name2 = DiagnosticManager.Current?.Name;
            Thread.Sleep(100);
        });

        Task.WaitAll(task1, task2);

        Assert.AreEqual("flow1", name1);
        Assert.AreEqual("flow2", name2);
    }
}
