using System.Collections.Concurrent;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Share.StateManagement;
using Share.TaskStatusPool.TaskPool;

namespace Share.StateManagement.Tests;

[TestClass]
public class TaskPoolTests
{
    private sealed record PoolItem(string TaskKey, int Priority = 0, int MaxRetries = 0) : ITaskPoolItem;

    private static async Task WaitUntilAsync(Func<Task<bool>> predicate, int timeoutMs = 8000)
    {
        var deadline = DateTime.UtcNow.AddMilliseconds(timeoutMs);
        while (DateTime.UtcNow < deadline)
        {
            if (await predicate().ConfigureAwait(false)) return;
            await Task.Delay(20).ConfigureAwait(false);
        }
        Assert.Fail($"等待超时（{timeoutMs}ms）");
    }

    private static Task<TaskExecutionStatus> Status(TaskPool<PoolItem, string> pool, string key)
        => pool.GetStatusAsync(key);

    [TestMethod]
    public async Task Enqueue_Activates_And_Completes()
    {
        var processed = new ConcurrentQueue<string>();
        var pool = new TaskPool<PoolItem, string>(
            new InMemoryTaskPoolPersistence<PoolItem>(),
            (item, execId, ct) => { processed.Enqueue(item.TaskKey); return Task.FromResult("ok"); },
            new TaskPoolOptions { MaxConcurrency = 1, MinTriggerIntervalMs = 0 });
        pool.Start();

        await pool.EnqueueAsync(new PoolItem("t1"));
        await WaitUntilAsync(async () => (await Status(pool, "t1")) == TaskExecutionStatus.Completed);

        CollectionAssert.AreEqual(new[] { "t1" }, processed.ToArray());
        await pool.DisposeAsync();
    }

    [TestMethod]
    public async Task Priority_Highest_First()
    {
        var order = new ConcurrentQueue<string>();
        var pool = new TaskPool<PoolItem, string>(
            new InMemoryTaskPoolPersistence<PoolItem>(),
            async (item, execId, ct) =>
            {
                await Task.Yield();
                order.Enqueue(item.TaskKey);
                return "ok";
            },
            new TaskPoolOptions { MaxConcurrency = 1, MinTriggerIntervalMs = 0 });
        pool.Start();

        await pool.EnqueueAsync(new PoolItem("low", Priority: 1));
        await pool.EnqueueAsync(new PoolItem("high", Priority: 100));
        await WaitUntilAsync(async () => (await Status(pool, "low")) == TaskExecutionStatus.Completed
                                        && (await Status(pool, "high")) == TaskExecutionStatus.Completed);

        CollectionAssert.AreEqual(new[] { "high", "low" }, order.ToArray());
        await pool.DisposeAsync();
    }

    [TestMethod]
    public async Task Queued_Pause_Resume_Requeues()
    {
        var pool = new TaskPool<PoolItem, string>(
            new InMemoryTaskPoolPersistence<PoolItem>(),
            (item, execId, ct) => Task.FromResult("ok"));
        // 不 Start，先入队再暂停 → 排队暂停（NotStarted → Paused）
        await pool.EnqueueAsync(new PoolItem("t2"));
        Assert.AreEqual(TaskExecutionStatus.NotStarted, await Status(pool, "t2"));

        await pool.PauseAsync("t2", "external", "queued-pause");
        Assert.AreEqual(TaskExecutionStatus.Paused, await Status(pool, "t2"));

        await pool.ResumeAsync("t2"); // Paused → NotStarted（重新可出队）
        Assert.AreEqual(TaskExecutionStatus.NotStarted, await Status(pool, "t2"));

        pool.Start();
        await WaitUntilAsync(async () => (await Status(pool, "t2")) == TaskExecutionStatus.Completed);
        await pool.DisposeAsync();
    }

    [TestMethod]
    public async Task InFlight_Pause_Blocks_Then_Resume_Completes()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        TaskPool<PoolItem, string>? poolRef = null;
        var pool = new TaskPool<PoolItem, string>(
            new InMemoryTaskPoolPersistence<PoolItem>(),
            async (item, execId, ct) =>
            {
                started.TrySetResult();
                await release.Task;                                   // 测试先暂停在途任务
                await poolRef!.WaitIfPausedAsync(item.TaskKey, ct);   // 此刻已暂停 → 阻塞至恢复
                return "ok";
            },
            new TaskPoolOptions { MaxConcurrency = 1, MinTriggerIntervalMs = 0 });
        poolRef = pool;
        pool.Start();

        await pool.EnqueueAsync(new PoolItem("t3"));
        await WaitUntilAsync(() => Task.FromResult(started.Task.IsCompleted)); // 处理体已进入（Running，在途）
        Assert.AreEqual(TaskExecutionStatus.Running, await Status(pool, "t3"));

        await pool.PauseAsync("t3", "external", "in-flight-pause"); // Running → Paused + TCS 挂起
        await Task.Delay(50);
        Assert.AreEqual(TaskExecutionStatus.Paused, await Status(pool, "t3"));

        release.TrySetResult(); // 放行 handler 进入挂起点 → 因已暂停而阻塞
        await Task.Delay(50);
        Assert.AreEqual(TaskExecutionStatus.Paused, await Status(pool, "t3"));

        await pool.ResumeAsync("t3"); // Paused → Running + 唤醒处理体
        await WaitUntilAsync(async () => (await Status(pool, "t3")) == TaskExecutionStatus.Completed);

        Assert.AreEqual(TaskExecutionStatus.Completed, await Status(pool, "t3"));
        await pool.DisposeAsync();
    }


    [TestMethod]
    public async Task Failure_Retries_Then_Completes()
    {
        var attempts = 0;
        var pool = new TaskPool<PoolItem, string>(
            new InMemoryTaskPoolPersistence<PoolItem>(),
            (item, execId, ct) =>
            {
                if (Interlocked.Increment(ref attempts) == 1)
                    throw new InvalidOperationException("first attempt fails");
                return Task.FromResult("ok");
            },
            new TaskPoolOptions { MaxConcurrency = 1, MinTriggerIntervalMs = 0 });
        pool.Start();

        await pool.EnqueueAsync(new PoolItem("t4", MaxRetries: 1));
        await WaitUntilAsync(async () => (await Status(pool, "t4")) == TaskExecutionStatus.Completed);

        Assert.AreEqual(2, attempts);
        await pool.DisposeAsync();
    }

    [TestMethod]
    public async Task Cancel_Terminates_InFlight()
    {
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var pool = new TaskPool<PoolItem, string>(
            new InMemoryTaskPoolPersistence<PoolItem>(),
            async (item, execId, ct) =>
            {
                started.TrySetResult();
                await Task.Delay(Timeout.Infinite, ct); // 靠 ct 取消
                return "ok";
            },
            new TaskPoolOptions { MaxConcurrency = 1, MinTriggerIntervalMs = 0 });
        pool.Start();

        await pool.EnqueueAsync(new PoolItem("t5"));
        await WaitUntilAsync(() => Task.FromResult(started.Task.IsCompleted));
        Assert.AreEqual(TaskExecutionStatus.Running, await Status(pool, "t5"));

        using var cts = new CancellationTokenSource();
        cts.CancelAfter(50);
        await pool.CancelAsync("t5", cts.Token);
        await WaitUntilAsync(async () => (await Status(pool, "t5")) == TaskExecutionStatus.Cancelled);
        await pool.DisposeAsync();
    }

    [TestMethod]
    public async Task ProcessPending_Requeues_Dead_Running()
    {
        // 模拟重启：持久化里有一条 Running（进程内已死），回填应转 NotStarted 重放
        var persistence = new InMemoryTaskPoolPersistence<PoolItem>();
        await persistence.EnqueueAsync(new PoolItem("t6"));
        await persistence.UpdateStatusAsync("t6", TaskExecutionStatus.NotStarted, TaskExecutionStatus.Running);

        var pool = new TaskPool<PoolItem, string>(
            persistence,
            (item, execId, ct) => Task.FromResult("ok"));
        pool.Start();

        await pool.ProcessPendingAsync();
        await WaitUntilAsync(async () => (await Status(pool, "t6")) == TaskExecutionStatus.Completed);
        await pool.DisposeAsync();
    }

    [TestMethod]
    public async Task SkipRateLimitFor_True_Immediate_Release()
    {
        // 方案 B：SkipRateLimitFor=true（零 LLM 任务等效）→ 跳过限频门、立即放行，不被窗口卡住。
        var processed = new ConcurrentQueue<string>();
        var pool = new TaskPool<PoolItem, string>(
            new InMemoryTaskPoolPersistence<PoolItem>(),
            (item, execId, ct) => { processed.Enqueue(item.TaskKey); return Task.FromResult("ok"); },
            new TaskPoolOptions { MaxConcurrency = 1, MinTriggerIntervalMs = 300, SkipRateLimitFor = _ => true });
        pool.Start();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await pool.EnqueueAsync(new PoolItem("t7"));
        await pool.EnqueueAsync(new PoolItem("t8"));
        await WaitUntilAsync(async () => (await Status(pool, "t7")) == TaskExecutionStatus.Completed);
        await WaitUntilAsync(async () => (await Status(pool, "t8")) == TaskExecutionStatus.Completed);
        sw.Stop();

        // 两个任务均跳过限频 → 总耗时应远小于 300ms 窗口（无窗口边界等待）。
        Assert.IsTrue(sw.Elapsed < TimeSpan.FromMilliseconds(250),
            $"SkipRateLimitFor=true 的任务仍被限频（耗时 {sw.Elapsed}）");
        CollectionAssert.AreEqual(new[] { "t7", "t8" }, processed.ToArray());
        await pool.DisposeAsync();
    }

    [TestMethod]
    public async Task SkipRateLimitFor_False_Preserves_RateLimit()
    {
        // 方案「真实路径行为不变」：SkipRateLimitFor=false（Hybrid 任务等效）→ 仍按窗口限频（第二个任务等待到窗口边界）。
        var processed = new ConcurrentQueue<string>();
        var pool = new TaskPool<PoolItem, string>(
            new InMemoryTaskPoolPersistence<PoolItem>(),
            (item, execId, ct) => { processed.Enqueue(item.TaskKey); return Task.FromResult("ok"); },
            new TaskPoolOptions { MaxConcurrency = 1, MinTriggerIntervalMs = 500, SkipRateLimitFor = _ => false });
        pool.Start();

        var sw = System.Diagnostics.Stopwatch.StartNew();
        await pool.EnqueueAsync(new PoolItem("t9"));
        await WaitUntilAsync(async () => (await Status(pool, "t9")) == TaskExecutionStatus.Completed);
        var t9Done = sw.Elapsed; // 进程首信号 → 立即放行

        await pool.EnqueueAsync(new PoolItem("t10"));
        await WaitUntilAsync(async () => (await Status(pool, "t10")) == TaskExecutionStatus.Completed);
        sw.Stop();
        var t10Done = sw.Elapsed;

        // 第二任务距首次放行 < 窗口 → 等待到窗口边界后放行；两任务完成间隔应 ≥ 窗口（含执行/调度容差）。
        var interval = t10Done - t9Done;
        Assert.IsTrue(interval >= TimeSpan.FromMilliseconds(380),
            $"真实路径任务未按窗口限频（间隔 {interval} < 380ms）");
        await pool.DisposeAsync();
    }

    }
