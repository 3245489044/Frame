
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.DiagnosticContext.Core;
using Modules.DiagnosticContext.Queue;
using BasicMethod.Queue;

namespace DiagnosticContext.Tests.Queue;

[TestClass]
public class EventPipelineTests
{
    [TestMethod]
    public async Task ExecuteAsync_应执行所有步骤()
    {
        var pipeline = new EventPipeline("test-pipeline");
        var steps = new List<string>();

        pipeline.AddHandler("step1", (EventEntry entry, CancellationToken ct) =>
        {
            steps.Add("step1");
            return Task.FromResult(QueueResult.Ok());
        });
        pipeline.AddHandler("step2", (EventEntry entry, CancellationToken ct) =>
        {
            steps.Add("step2");
            return Task.FromResult(QueueResult.Ok());
        });

        var entry = new EventEntry("test.event", 100);
        await pipeline.ExecuteAsync(entry);

        Assert.AreEqual(2, steps.Count);
        Assert.IsTrue(steps.Contains("step1"));
        Assert.IsTrue(steps.Contains("step2"));
    }

    [TestMethod]
    public async Task ExecuteAsync_应保持步骤顺序()
    {
        var pipeline = new EventPipeline("test-pipeline");
        var order = new List<string>();

        pipeline.AddHandler("first", (EventEntry entry, CancellationToken ct) =>
        {
            order.Add("first");
            return Task.FromResult(QueueResult.Ok());
        });
        pipeline.AddHandler("second", (EventEntry entry, CancellationToken ct) =>
        {
            order.Add("second");
            return Task.FromResult(QueueResult.Ok());
        });
        pipeline.AddHandler("third", (EventEntry entry, CancellationToken ct) =>
        {
            order.Add("third");
            return Task.FromResult(QueueResult.Ok());
        });

        var entry = new EventEntry("test.event", 0);
        await pipeline.ExecuteAsync(entry);

        Assert.AreEqual(3, order.Count);
        Assert.AreEqual("first", order[0]);
        Assert.AreEqual("second", order[1]);
        Assert.AreEqual("third", order[2]);
    }

    [TestMethod]
    public async Task Remove_应移除指定步骤()
    {
        var pipeline = new EventPipeline("test-pipeline");
        var steps = new List<string>();

        pipeline.AddHandler("step1", (EventEntry entry, CancellationToken ct) =>
        {
            steps.Add("step1");
            return Task.FromResult(QueueResult.Ok());
        });
        pipeline.AddHandler("step2", (EventEntry entry, CancellationToken ct) =>
        {
            steps.Add("step2");
            return Task.FromResult(QueueResult.Ok());
        });

        pipeline.Remove("step1");

        var entry = new EventEntry("test.event", 0);
        await pipeline.ExecuteAsync(entry);

        Assert.AreEqual(1, steps.Count);
        Assert.AreEqual("step2", steps[0]);
    }

    [TestMethod]
    public async Task InsertBefore_应插入到指定步骤之前()
    {
        var pipeline = new EventPipeline("test-pipeline");
        var order = new List<string>();

        pipeline.AddHandler("second", (EventEntry entry, CancellationToken ct) =>
        {
            order.Add("second");
            return Task.FromResult(QueueResult.Ok());
        });
        pipeline.InsertBefore("second", (EventEntry entry, CancellationToken ct) =>
        {
            order.Add("first");
            return Task.FromResult(QueueResult.Ok());
        });

        var entry = new EventEntry("test.event", 0);
        await pipeline.ExecuteAsync(entry);

        Assert.AreEqual(2, order.Count);
        Assert.AreEqual("first", order[0]);
        Assert.AreEqual("second", order[1]);
    }

    [TestMethod]
    public async Task InsertAfter_应插入到指定步骤之后()
    {
        var pipeline = new EventPipeline("test-pipeline");
        var order = new List<string>();

        pipeline.AddHandler("first", (EventEntry entry, CancellationToken ct) =>
        {
            order.Add("first");
            return Task.FromResult(QueueResult.Ok());
        });
        pipeline.InsertAfter("first", (EventEntry entry, CancellationToken ct) =>
        {
            order.Add("second");
            return Task.FromResult(QueueResult.Ok());
        });

        var entry = new EventEntry("test.event", 0);
        await pipeline.ExecuteAsync(entry);

        Assert.AreEqual(2, order.Count);
        Assert.AreEqual("first", order[0]);
        Assert.AreEqual("second", order[1]);
    }

    [TestMethod]
    public async Task 同步处理器_应正常工作()
    {
        var pipeline = new EventPipeline("test-pipeline");
        var executed = false;

        pipeline.AddHandler("sync", (EventEntry entry) =>
        {
            executed = true;
            return QueueResult.Ok();
        });

        var entry = new EventEntry("test.event", 0);
        await pipeline.ExecuteAsync(entry);

        Assert.IsTrue(executed);
    }

    [TestMethod]
    public void 管线名称_应正确设置()
    {
        var pipeline = new EventPipeline("my-pipeline");

        Assert.AreEqual("my-pipeline", pipeline.Name);
    }

    [TestMethod]
    public void Handlers_应返回只读列表()
    {
        var pipeline = new EventPipeline("test-pipeline");

        pipeline.AddHandler("step1", (EventEntry entry, CancellationToken ct) =>
            Task.FromResult(QueueResult.Ok()));

        var handlers = pipeline.Handlers;
        Assert.AreEqual(1, handlers.Count);
    }

    [TestMethod]
    public void Remove_不存在的步骤_应静默忽略()
    {
        var pipeline = new EventPipeline("test-pipeline");
        pipeline.Remove("nonexistent");
    }
}
