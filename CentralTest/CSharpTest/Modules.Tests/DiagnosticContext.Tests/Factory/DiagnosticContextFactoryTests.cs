
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.DiagnosticContext.Core;
using Modules.DiagnosticContext.Factory;
using Modules.DiagnosticContext.Configuration;

namespace DiagnosticContext.Tests.Factory;

[TestClass]
public class DiagnosticContextFactoryTests
{
    [TestMethod]
    public void Create_默认配置_应创建上下文()
    {
        using var ctx = DiagnosticContextFactory.Create("test");

        Assert.IsNotNull(ctx);
        Assert.AreEqual("test", ctx.Name);
    }

    [TestMethod]
    public void Create_使用配置对象_应应用配置()
    {
        var config = new DiagnosticContextConfiguration
        {
            EnableEventQueue = false,
            OutputTarget = OutputTarget.None
        };

        using var ctx = DiagnosticContextFactory.Create("test", config);

        Assert.IsNotNull(ctx);
        Assert.AreEqual("test", ctx.Name);
    }

    [TestMethod]
    public void Create_使用委托配置_应应用配置()
    {
        using var ctx = DiagnosticContextFactory.Create("test", cfg =>
        {
            cfg.EnableEventQueue = false;
        });

        Assert.IsNotNull(ctx);
        Assert.AreEqual("test", ctx.Name);
    }

    [TestMethod]
    public void Create_启用事件队列_应装配EventQueue()
    {
        var config = new DiagnosticContextConfiguration
        {
            EnableEventQueue = true
        };

        using var ctx = DiagnosticContextFactory.Create("test", config);

        Assert.IsNotNull(ctx);
        ctx.Record("test.event");
    }

    [TestMethod]
    public void Create_启用分发器_应装配EventDispatcher()
    {
        var config = new DiagnosticContextConfiguration
        {
            EnableEventQueue = true,
            EnableDispatcher = true
        };

        using var ctx = DiagnosticContextFactory.Create("test", config);

        Assert.IsNotNull(ctx);
        ctx.Record("test.event");
    }

    [TestMethod]
    public void Create_启用Pipeline_应装配EventPipeline()
    {
        var config = new DiagnosticContextConfiguration
        {
            EnableEventQueue = true,
            EnablePipeline = true
        };

        using var ctx = DiagnosticContextFactory.Create("test", config);

        Assert.IsNotNull(ctx);
        ctx.Record("test.event");
    }

    [TestMethod]
    public void Create_名称null_应抛出ArgumentNullException()
    {
        Assert.ThrowsException<ArgumentNullException>(() => DiagnosticContextFactory.Create(null!));
    }

    [TestMethod]
    public void Create_名称空字符串_应允许()
    {
        using var ctx = DiagnosticContextFactory.Create("");

        Assert.IsNotNull(ctx);
        Assert.AreEqual("", ctx.Name);
    }

    [TestMethod]
    public void Create_配置null_应使用默认配置()
    {
        using var ctx = DiagnosticContextFactory.Create("test", (DiagnosticContextConfiguration?)null!);

        Assert.IsNotNull(ctx);
        Assert.AreEqual("test", ctx.Name);
    }

    [TestMethod]
    public void Create_批量队列类型_应装配BatchEventQueue()
    {
        var config = new DiagnosticContextConfiguration
        {
            EnableEventQueue = true,
            QueueType = EventQueueType.Batch,
            BatchSize = 10,
            FlushInterval = TimeSpan.FromMilliseconds(100)
        };

        using var ctx = DiagnosticContextFactory.Create("test", config);

        Assert.IsNotNull(ctx);
        ctx.Record("test.event");
    }

    [TestMethod]
    public void Create_包含前缀过滤器_应过滤事件()
    {
        var config = new DiagnosticContextConfiguration
        {
            EnableEventQueue = true,
            EnablePipeline = true,
            IncludeEventPrefix = "app."
        };

        using var ctx = DiagnosticContextFactory.Create("test", config);

        Assert.IsNotNull(ctx);
        ctx.Record("app.start");
        ctx.Record("other.event");
    }

    [TestMethod]
    public void Create_排除前缀过滤器_应过滤事件()
    {
        var config = new DiagnosticContextConfiguration
        {
            EnableEventQueue = true,
            EnablePipeline = true,
            ExcludeEventPrefix = "debug."
        };

        using var ctx = DiagnosticContextFactory.Create("test", config);

        Assert.IsNotNull(ctx);
        ctx.Record("app.start");
        ctx.Record("debug.info");
    }
}
