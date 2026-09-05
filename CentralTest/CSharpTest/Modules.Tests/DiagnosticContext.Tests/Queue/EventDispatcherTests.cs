
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.DiagnosticContext.Core;
using Modules.DiagnosticContext.Queue;
using BasicMethod.Queue;

namespace DiagnosticContext.Tests.Queue;

[TestClass]
public class EventDispatcherTests
{
    private EventDispatcher? _dispatcher;

    [TestCleanup]
    public void Cleanup()
    {
        _dispatcher?.Dispose();
    }

    [TestMethod]
    public async Task DispatchAsync_应分发事件到已注册处理器()
    {
        using var dispatcher = new EventDispatcher("test-dispatcher");
        _dispatcher = dispatcher;
        var handled = false;
        var tcs = new TaskCompletionSource<bool>();

        dispatcher.Register("test.event", (EventEntry entry, CancellationToken ct) =>
        {
            handled = true;
            tcs.TrySetResult(true);
            return Task.FromResult(QueueResult.Ok());
        });

        var entry = new EventEntry("test.event", 100);
        await dispatcher.DispatchAsync("test.event", entry);

        await tcs.Task.WaitAsync(TimeSpan.FromSeconds(5));

        Assert.IsTrue(handled);
    }

    [TestMethod]
    public async Task DispatchAsync_未注册事件_应静默忽略()
    {
        using var dispatcher = new EventDispatcher("test-dispatcher");
        _dispatcher = dispatcher;
        var entry = new EventEntry("unregistered", 100);

        var result = await dispatcher.DispatchAsync("unregistered", entry);

        Assert.IsFalse(result.IsSuccess);
    }

    [TestMethod]
    public async Task DispatchAsync_应分发到事件队列()
    {
        using var dispatcher = new EventDispatcher("test-dispatcher");
        _dispatcher = dispatcher;
        var queue = new EventQueue("test-queue");

        dispatcher.Register("test.event", queue);

        var entry = new EventEntry("test.event", 100);
        await dispatcher.DispatchAsync("test.event", entry);

        Assert.AreEqual(1, queue.Count);
    }

    [TestMethod]
    public void IsRegistered_应正确判断()
    {
        using var dispatcher = new EventDispatcher("test-dispatcher");
        _dispatcher = dispatcher;

        dispatcher.Register("test.event", (EventEntry entry, CancellationToken ct) =>
            Task.FromResult(QueueResult.Ok()));

        Assert.IsTrue(dispatcher.IsRegistered("test.event"));
        Assert.IsFalse(dispatcher.IsRegistered("nonexistent"));
    }

    [TestMethod]
    public void GetRegisteredKeys_应返回所有注册键()
    {
        using var dispatcher = new EventDispatcher("test-dispatcher");
        _dispatcher = dispatcher;

        dispatcher.Register("event1", (EventEntry entry, CancellationToken ct) =>
            Task.FromResult(QueueResult.Ok()));
        dispatcher.Register("event2", (EventEntry entry, CancellationToken ct) =>
            Task.FromResult(QueueResult.Ok()));

        var keys = dispatcher.GetRegisteredKeys();
        Assert.AreEqual(2, keys.Count);
        Assert.IsTrue(keys.Contains("event1"));
        Assert.IsTrue(keys.Contains("event2"));
    }

    [TestMethod]
    public async Task Unregister_应移除处理器()
    {
        using var dispatcher = new EventDispatcher("test-dispatcher");
        _dispatcher = dispatcher;
        var callCount = 0;

        dispatcher.Register("test.event", (EventEntry entry, CancellationToken ct) =>
        {
            Interlocked.Increment(ref callCount);
            return Task.FromResult(QueueResult.Ok());
        });

        dispatcher.Unregister("test.event");

        var entry = new EventEntry("test.event", 100);
        var result = await dispatcher.DispatchAsync("test.event", entry);

        Assert.IsFalse(result.IsSuccess);
        Assert.AreEqual(0, callCount);
    }

    [TestMethod]
    public async Task DispatchBatchAsync_应批量分发()
    {
        using var dispatcher = new EventDispatcher("test-dispatcher");
        _dispatcher = dispatcher;
        var queue = new EventQueue("test-queue");

        dispatcher.Register("test.event", queue);

        var events = new[]
        {
            new EventEntry("test.event", 0),
            new EventEntry("test.event", 1),
            new EventEntry("test.event", 2)
        };

        await dispatcher.DispatchBatchAsync("test.event", events);

        Assert.AreEqual(3, queue.Count);
    }

    [TestMethod]
    public void 分发器名称_应正确设置()
    {
        using var dispatcher = new EventDispatcher("my-dispatcher");
        _dispatcher = dispatcher;

        Assert.AreEqual("my-dispatcher", dispatcher.Name);
    }

    [TestMethod]
    public void Register_链式调用_应返回自身()
    {
        using var dispatcher = new EventDispatcher("test");
        _dispatcher = dispatcher;

        var result = dispatcher.Register("event1", (EventEntry entry, CancellationToken ct) =>
            Task.FromResult(QueueResult.Ok()));

        Assert.AreSame(dispatcher, result);
    }
}
