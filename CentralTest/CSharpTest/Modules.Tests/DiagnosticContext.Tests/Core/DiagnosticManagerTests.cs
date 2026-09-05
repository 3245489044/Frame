
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.DiagnosticContext.Core;
using Modules.DiagnosticContext.Factory;

namespace DiagnosticContext.Tests.Core;

[TestClass]
public class DiagnosticManagerTests
{
    [TestCleanup]
    public void Cleanup()
    {
        var current = DiagnosticManager.Current;
        current?.Dispose();
    }

    [TestMethod]
    public void BeginScope_应创建上下文并激活()
    {
        using var ctx = DiagnosticManager.BeginScope("test-scope");

        Assert.IsNotNull(ctx);
        Assert.AreEqual("test-scope", ctx.Name);
        Assert.AreSame(ctx, DiagnosticManager.Current);
    }

    [TestMethod]
    public void BeginScope_使用委托配置_应应用配置()
    {
        using var ctx = DiagnosticManager.BeginScope("test", cfg =>
        {
            cfg.EnableEventQueue = false;
        });

        Assert.IsNotNull(ctx);
        Assert.AreEqual("test", ctx.Name);
    }

    [TestMethod]
    public void Current_无活跃上下文_应返回Null()
    {
        var current = DiagnosticManager.Current;
        current?.Dispose();

        Assert.IsNull(DiagnosticManager.Current);
    }

    [TestMethod]
    public void Current_有活跃上下文_应返回当前上下文()
    {
        using var ctx = DiagnosticManager.BeginScope("test");

        Assert.AreSame(ctx, DiagnosticManager.Current);
    }

    [TestMethod]
    public void FirstActive_有活跃上下文_应返回第一个()
    {
        using var ctx = DiagnosticManager.BeginScope("test");

        Assert.IsNotNull(DiagnosticManager.FirstActive);
        Assert.AreEqual("test", DiagnosticManager.FirstActive!.Name);
    }

    [TestMethod]
    public void RecordToAll_应广播到所有活跃上下文()
    {
        using var ctx1 = DiagnosticManager.BeginScope("ctx1");
        using var ctx2 = DiagnosticManager.BeginScope("ctx2");

        DiagnosticManager.RecordToAll("broadcast.event");

        Assert.AreEqual(1, ctx1.RootScope.Events.Count);
        Assert.AreEqual(1, ctx2.RootScope.Events.Count);
        Assert.AreEqual("broadcast.event", ctx1.RootScope.Events[0].EventName);
        Assert.AreEqual("broadcast.event", ctx2.RootScope.Events[0].EventName);
    }

    [TestMethod]
    public void GetSnapshot_有活跃上下文_应返回快照文本()
    {
        using var ctx = DiagnosticManager.BeginScope("test");
        ctx.Record("my.event");

        var snapshot = DiagnosticManager.GetSnapshot();

        Assert.IsNotNull(snapshot);
        Assert.IsTrue(snapshot.Contains("test"));
        Assert.IsTrue(snapshot.Contains("my.event"));
    }

    [TestMethod]
    public void GetSnapshot_无活跃上下文_应返回提示文本()
    {
        var current = DiagnosticManager.Current;
        current?.Dispose();

        var snapshot = DiagnosticManager.GetSnapshot();

        Assert.AreEqual("(no active contexts)", snapshot);
    }

    [TestMethod]
    public void Configure_应影响后续创建的上下文()
    {
        DiagnosticManager.Configure(cfg =>
        {
            cfg.EnableEventQueue = false;
            cfg.OutputTarget = Modules.DiagnosticContext.Configuration.OutputTarget.None;
        });

        using var ctx = DiagnosticManager.BeginScope("configured-test");

        Assert.IsNotNull(ctx);
        Assert.AreEqual("configured-test", ctx.Name);
    }

    [TestMethod]
    public void BeginScope_Dispose后_Current应恢复为Null()
    {
        var previous = DiagnosticManager.Current;

        using (var ctx = DiagnosticManager.BeginScope("temp"))
        {
            Assert.IsNotNull(DiagnosticManager.Current);
        }

        Assert.AreSame(previous, DiagnosticManager.Current);
    }
}
