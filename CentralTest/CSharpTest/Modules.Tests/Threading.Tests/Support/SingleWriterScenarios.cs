using System.Collections.Concurrent;
using Modules.Threading;

namespace Modules.Threading.Tests.Support;

/// <summary>
/// 单写 actor 的可复用并发测试脚手架：把「并发 N 项必须串行于单一 worker 写线程、执行线程必须等于资源创建线程、
/// worker 就绪等待」这类<b>易错且易死锁</b>的断言收进来，后续 actor 相关测试只调用场景、不再手写
/// barrier / Yield / 线程身份判断。框架已把工作体限定为<b>同步体</b>（构造性防死锁），本脚手架据此断言单线程纪律。
/// </summary>
public static class SingleWriterScenarios
{
    /// <summary>并发提交结果快照。</summary>
    public sealed record ConcurrentRun(int Count, int WorkerThreadId);

    /// <summary>轮询等待 worker 就绪（资源已在其线程创建），避免测试在资源未建好前提交导致竞态。</summary>
    public static async Task WaitWorkerReadyAsync(Func<bool> ready, int timeoutMs = 3000)
    {
        ArgumentNullException.ThrowIfNull(ready);
        var deadline = Environment.TickCount64 + timeoutMs;
        while (Environment.TickCount64 < deadline)
        {
            if (ready()) return;
            await Task.Delay(10).ConfigureAwait(false);
        }
        throw new TimeoutException($"等待条件超时（{timeoutMs}ms）。");
    }

    /// <summary>
    /// 并发提交 <paramref name="count"/> 个同步体，断言：全部执行无丢失、且全部串行于<b>同一根</b> worker 线程、
    /// 该线程即资源创建线程（线程亲和）。返回执行计数与 worker 线程 id，供调用方附加断言。
    /// </summary>
    public static async Task<ConcurrentRun> AssertConcurrentSerializedAsync<TResource>(
        Func<TResource> create, int count, Action<TResource> body)
        where TResource : class
    {
        ArgumentNullException.ThrowIfNull(create);
        ArgumentNullException.ThrowIfNull(body);
        if (count <= 0) throw new ArgumentOutOfRangeException(nameof(count));

        int workerCreateThread = 0;
        var opThreads = new ConcurrentQueue<int>();
        using var actor = new SingleWriterActor<TResource>(
            create: () =>
            {
                Interlocked.Exchange(ref workerCreateThread, Thread.CurrentThread.ManagedThreadId);
                return create();
            },
            name: "scenario-concurrent",
            dispose: _ => { });
        actor.Start();
        await WaitWorkerReadyAsync(() => Interlocked.CompareExchange(ref workerCreateThread, 0, 0) != 0);

        var tasks = new Task[count];
        for (var i = 0; i < count; i++)
        {
            tasks[i] = actor.ExecuteAsync(r => { opThreads.Enqueue(Thread.CurrentThread.ManagedThreadId); body(r); });
        }
        await Task.WhenAll(tasks);

        var ids = opThreads.Distinct().ToArray();
        if (ids.Length != 1)
            throw new InvalidOperationException($"并发工作未串行于单一 worker 写线程（出现 {ids.Length} 个执行线程：{string.Join(',', ids)}）。");
        if (ids[0] != workerCreateThread)
            throw new InvalidOperationException($"执行线程({ids[0]})与资源创建线程({workerCreateThread})不一致，资源离线程。");
        return new ConcurrentRun(count, ids[0]);
    }
}
