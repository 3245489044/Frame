
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.DiagnosticContext.Core;
using Modules.DiagnosticContext.Queue;

namespace DiagnosticContext.Tests.Queue;

[TestClass]
public class EventQueueTests
{
    [TestMethod]
    public async Task EnqueueAsync_应入队事件()
    {
        var queue = new EventQueue("test-queue");
        var entry = new EventEntry("test.event", 100);

        await queue.EnqueueAsync(entry);

        Assert.AreEqual(1, queue.Count);
    }

    [TestMethod]
    public async Task EnqueueAsync_多次入队_应增加计数()
    {
        var queue = new EventQueue("test-queue");

        await queue.EnqueueAsync(new EventEntry("e1", 0));
        await queue.EnqueueAsync(new EventEntry("e2", 0));
        await queue.EnqueueAsync(new EventEntry("e3", 0));

        Assert.AreEqual(3, queue.Count);
    }

    [TestMethod]
    public async Task TryDequeueAsync_应出队事件()
    {
        var queue = new EventQueue("test-queue");
        var entry = new EventEntry("test.event", 100);
        await queue.EnqueueAsync(entry);

        var (success, dequeued) = await queue.TryDequeueAsync();

        Assert.IsTrue(success);
        Assert.IsNotNull(dequeued);
        Assert.AreEqual("test.event", dequeued!.EventName);
        Assert.AreEqual(0, queue.Count);
    }

    [TestMethod]
    public async Task TryDequeueAsync_空队列_应返回失败()
    {
        var queue = new EventQueue("test-queue");

        var (success, dequeued) = await queue.TryDequeueAsync();

        Assert.IsFalse(success);
        Assert.IsNull(dequeued);
    }

    [TestMethod]
    public async Task TryDequeueAsync_应保持FIFO顺序()
    {
        var queue = new EventQueue("test-queue");
        await queue.EnqueueAsync(new EventEntry("first", 0));
        await queue.EnqueueAsync(new EventEntry("second", 0));
        await queue.EnqueueAsync(new EventEntry("third", 0));

        var (_, r1) = await queue.TryDequeueAsync();
        var (_, r2) = await queue.TryDequeueAsync();
        var (_, r3) = await queue.TryDequeueAsync();

        Assert.AreEqual("first", r1!.EventName);
        Assert.AreEqual("second", r2!.EventName);
        Assert.AreEqual("third", r3!.EventName);
    }

    [TestMethod]
    public async Task Clear_应清空队列()
    {
        var queue = new EventQueue("test-queue");
        await queue.EnqueueAsync(new EventEntry("e1", 0));
        await queue.EnqueueAsync(new EventEntry("e2", 0));

        queue.Clear();

        Assert.AreEqual(0, queue.Count);
    }

    [TestMethod]
    public async Task StartConsumingAsync_应消费事件()
    {
        var queue = new EventQueue("test-queue");
        await queue.EnqueueAsync(new EventEntry("e1", 0));
        await queue.EnqueueAsync(new EventEntry("e2", 0));

        var consumed = new List<EventEntry>();
        await queue.StartConsumingAsync(async (entry, ct) =>
        {
            consumed.Add(entry);
            return BasicMethod.Queue.QueueResult.Ok();
        });

        await Task.Delay(200);
        await queue.StopConsumingAsync();

        Assert.AreEqual(2, consumed.Count);
    }

    [TestMethod]
    public void 新队列_Count应为0()
    {
        var queue = new EventQueue("test-queue");

        Assert.AreEqual(0, queue.Count);
    }

    [TestMethod]
    public void 队列名称_应正确设置()
    {
        var queue = new EventQueue("my-queue");

        Assert.AreEqual("my-queue", queue.Name);
    }

    [TestMethod]
    public void 使用外部队列_应正确包装()
    {
        var inner = new BasicMethod.Queue.MemoryQueue<EventEntry>("inner-queue");
        var queue = new EventQueue(inner);

        Assert.AreEqual("inner-queue", queue.Name);
    }
}
