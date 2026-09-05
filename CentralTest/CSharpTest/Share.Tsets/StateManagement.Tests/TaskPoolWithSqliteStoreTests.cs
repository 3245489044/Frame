using System.Collections.Concurrent;
using System.IO;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Operation.DataStoreDriven.Sqlite;
using UniversalWorkflowFramework.StorageFramework.Sqlite;
using Share.StateManagement;
using Share.TaskStatusPool.TaskPool;

namespace Share.StateManagement.Tests;

/// <summary>
/// 通用任务池 + Sqlite 持久化存储（ur_tasks）集成测试：直接验证三条能力——
/// ①创建任务 → Agent/处理器自动识别执行；②在途/排队暂停与恢复；③队列级全局暂停/恢复；④重启回填续跑。
/// </summary>
[TestClass]
public class TaskPoolWithSqliteStoreTests
{
    private sealed record PoolItem(string TaskKey, int Priority = 0, int MaxRetries = 0) : ITaskPoolItem;

    private static string NewDb() => Path.Combine(Path.GetTempPath(), $"tp_{System.Guid.NewGuid():N}.db");

    private static SqliteTaskPoolStore<PoolItem> Store(string db)
        => new(new SqliteSession($"Data Source={db};Version=3;"), "ur_");

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
    public async Task Enqueue_AutoExecutes_And_Completes_With_SqliteStore()
    {
        var db = NewDb();
        var processed = new ConcurrentQueue<string>();
        try
        {
            var persistence = Store(db);
            await persistence.InitializeAsync();
            var pool = new TaskPool<PoolItem, string>(
                persistence,
                (item, execId, ct) => { processed.Enqueue(item.TaskKey); return Task.FromResult("ok"); },
                new TaskPoolOptions { MaxConcurrency = 1, MinTriggerIntervalMs = 0 });
            pool.Start();

            await pool.EnqueueAsync(new PoolItem("t1"));
            await WaitUntilAsync(async () => (await Status(pool, "t1")) == TaskExecutionStatus.Completed);

            CollectionAssert.AreEqual(new[] { "t1" }, processed.ToArray());
            await pool.DisposeAsync();
        }
        finally { File.Delete(db); }
    }

    [TestMethod]
    public async Task GlobalPause_Stops_Dispatch_Then_Resume_Completes()
    {
        var db = NewDb();
        var order = new ConcurrentQueue<string>();
        var t1Started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var release = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        try
        {
            var persistence = Store(db);
            await persistence.InitializeAsync();
            TaskPool<PoolItem, string>? pool = null; // 先声明后赋值：handler 仅在 Start+Enqueue 后执行，捕获引用此时已非空
            pool = new TaskPool<PoolItem, string>(
                persistence,
                async (item, execId, ct) =>
                {
                    order.Enqueue(item.TaskKey);
                    if (item.TaskKey == "t1") t1Started.TrySetResult();
                    await release.Task.ConfigureAwait(false);          // 保持 t1 在途（Running）
                    await pool!.WaitIfPausedAsync(item.TaskKey, ct).ConfigureAwait(false); // 合作式挂点（Agent 轮间暂停的对等点）
                    return "ok";
                },
                new TaskPoolOptions { MaxConcurrency = 1, MinTriggerIntervalMs = 0 });
            pool.Start();

            await pool.EnqueueAsync(new PoolItem("t1"));
            await pool.EnqueueAsync(new PoolItem("t2"));
            await WaitUntilAsync(() => Task.FromResult(t1Started.Task.IsCompleted));
            await WaitUntilAsync(async () => (await Status(pool, "t1")) == TaskExecutionStatus.Running
                                            && (await Status(pool, "t2")) == TaskExecutionStatus.NotStarted);

            // 队列级全局暂停：在途 t1（Running）与排队 t2（NotStarted）均转 Paused，均不完成
            var paused = await pool.PauseAllAsync();
            Assert.IsTrue(paused >= 2);
            release.TrySetResult(); // 放行 t1 进入合作式挂点 → 因已暂停而阻塞
            await Task.Delay(50);
            Assert.AreEqual(TaskExecutionStatus.Paused, await Status(pool, "t1"));
            Assert.AreEqual(TaskExecutionStatus.Paused, await Status(pool, "t2"));

            // 恢复：t1 唤醒继续完成；t2 重新可出队 → 完成
            await pool.ResumeAllAsync();
            await WaitUntilAsync(async () => (await Status(pool, "t1")) == TaskExecutionStatus.Completed
                                            && (await Status(pool, "t2")) == TaskExecutionStatus.Completed);

            CollectionAssert.AreEqual(new[] { "t1", "t2" }, order.ToArray());
            await pool.DisposeAsync();
        }
        finally { File.Delete(db); }
    }

    [TestMethod]
    public async Task ProcessPending_After_Restart_Replays_NotStarted()
    {
        var db = NewDb();
        var processed = new ConcurrentQueue<string>();
        try
        {
            // 第一进程：入队 t1 并置为 Running（模拟崩溃时在途，进程已死）
            var first = Store(db);
            await first.InitializeAsync();
            await first.EnqueueAsync(new PoolItem("t1"));
            await first.UpdateStatusAsync("t1", TaskExecutionStatus.NotStarted, TaskExecutionStatus.Running);

            // 第二进程（重启）：新池 + 新存储实例，ProcessPending 把死 Running 转 NotStarted 重放
            var second = Store(db);
            await second.InitializeAsync();
            var pool = new TaskPool<PoolItem, string>(
                second,
                (item, execId, ct) => { processed.Enqueue(item.TaskKey); return Task.FromResult("ok"); },
                new TaskPoolOptions { MaxConcurrency = 1, MinTriggerIntervalMs = 0 });
            pool.Start();

            await pool.ProcessPendingAsync();
            await WaitUntilAsync(async () => (await Status(pool, "t1")) == TaskExecutionStatus.Completed);

            CollectionAssert.AreEqual(new[] { "t1" }, processed.ToArray());
            await pool.DisposeAsync();
        }
        finally { File.Delete(db); }
    }
}
