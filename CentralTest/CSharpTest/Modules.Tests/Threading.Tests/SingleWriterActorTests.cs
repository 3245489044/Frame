using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Modules.Threading;
using Modules.Threading.Tests.Support;

namespace Modules.Threading.Tests;

/// <summary>
/// 单写 actor 线程框架（<see cref="SingleWriterActor{TResource}"/>）回归。
/// 并发纪律断言复用 <see cref="SingleWriterScenarios"/> 脚手架（不手写 barrier/线程判断），
/// 规避「手写并发测试易死锁」类问题。覆盖：并发串行单写线程 · worker 内联重入无死锁 ·
/// 取消 · 资源线程亲和护栏 · 停止后拒收。
/// </summary>
[TestClass]
public class SingleWriterActorTests
{
    [TestMethod]
    public async Task ConcurrentSubmitters_AllSerializedOnSingleWorker_NoLoss()
    {
        int value = 0;
        const int n = 200;
        var run = await SingleWriterScenarios.AssertConcurrentSerializedAsync(
            create: () => new object(),
            count: n,
            body: _ => Interlocked.Increment(ref value));

        Assert.AreEqual(n, Volatile.Read(ref value), "全部提交应完整执行，无死信/丢失。");
        Assert.AreEqual(n, run.Count, "执行计数应与提交数一致。");
    }

    [TestMethod]
    public async Task WorkerInlineReentry_SyncAndAsync_NoSelfDeadlock()
    {
        int stage = 0;
        bool sawWorker = false;
        using var actor = new SingleWriterActor<object>(() => new object(), "swa-reentry");
        actor.Start();

        // 工作体为同步体（构造性防死锁）；在 worker 内联重入（同步 + 异步入口）均不得入队自等。
        await actor.ExecuteAsync(_ =>
        {
            Assert.IsTrue(actor.IsCurrentWorker, "工作体应运行在 worker 线程。");
            sawWorker = true;
            actor.Execute(__ => Interlocked.Exchange(ref stage, 1));
            actor.ExecuteAsync(__ => Interlocked.Exchange(ref stage, 2)).GetAwaiter().GetResult();
        });

        Assert.IsTrue(sawWorker);
        Assert.AreEqual(2, Volatile.Read(ref stage), "worker 内联重入应完整执行（同步与异步入口两层）。");
    }

    [TestMethod]
    public async Task CancelledToken_WhileQueued_CancelsWait()
    {
        using var actor = new SingleWriterActor<object>(() => new object(), "swa-cancel");
        actor.Start();
        using var cts = new CancellationTokenSource();
        cts.Cancel();
        await Assert.ThrowsAsync<OperationCanceledException>(() => actor.ExecuteAsync(_ => 0, cts.Token));
    }

    [TestMethod]
    public void Resource_OnlyWorkerThread_CanAccess()
    {
        var actor = new SingleWriterActor<object>(() => new object(), "swa-affinity");
        // 调用方线程（非 worker）访问资源 → 护栏拒绝（线程亲和不变式）。
        Assert.ThrowsException<InvalidOperationException>(() => actor.Resource);
    }

    [TestMethod]
    public void Stop_RejectsNewSubmits()
    {
        var actor = new SingleWriterActor<object>(() => new object(), "swa-dispose");
        actor.Start();
        actor.Stop();
        Assert.ThrowsException<InvalidOperationException>(() => actor.Execute(_ => 0));
    }
}
