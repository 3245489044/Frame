using System;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Threading.Timing;

/// <summary>
/// 有界真竞速超时原语（DUP-2 收敛，自 Agent.Core 迁入）—— linked-CTS + CancelAfter + WhenAny(body, Delay(Infinite)) 的单一权威实现。
/// 供真竞速有界执行点复用（单步 / 单轮 / 轮后钩子），把「外层取消优先、预算超时 vs 取消二分类」统一于此；
/// 各调用方按自身策略映射超时尾处理（抛强类型异常 / 置 Timeout 状态 / 优雅排空）。
/// <para>
/// 命名避撞：<see cref="Modules.Threading.BoundedExecution"/>（RunRoundAsync + BoundedTimeoutException，对象生命周期语义）为
/// 另一机制；本类型只承担「Race&lt;T&gt; 三态竞速」原语，命名 BoundedRace 以与 sibling 区分。
/// </para>
/// <para>
/// 注：NodePersistOutbox 的逐条落库超时【有意不竞速】—— 竞速弃写会把单写 DB 持久化留在半提交态，故维持协作式取消
/// （仅登记，不收敛进本原语），对齐其「死信不阻塞后续、绝不阻塞主循环」语义。
/// </para>
/// </summary>
public static class BoundedRace
{
    /// <summary>有界竞速结果：body 是否胜出 / 链路令牌是否已被取消（预算到时或外层取消）/ 是否外层取消；<see cref="Body"/> 供取结果或排空。</summary>
    public sealed class Race<T> : IDisposable
    {
        private readonly CancellationTokenSource _linked;
        /// <summary>body 在预算内完成（胜出）。</summary>
        public bool Completed { get; }
        /// <summary>链路令牌在裁决时已被取消（CancelAfter 到时或外层取消触发）。</summary>
        public bool TokenCanceled { get; }
        /// <summary>裁决时外层令牌已请求取消（外层取消优先于预算超时）。</summary>
        public bool OuterCanceled { get; }
        /// <summary>起始的 body 任务（胜出取结果 / 败出时排空或交取消语义）。</summary>
        public Task<T> Body { get; }

        internal Race(CancellationTokenSource linked, bool completed, bool tokenCanceled, bool outerCanceled, Task<T> body)
        {
            _linked = linked;
            Completed = completed;
            TokenCanceled = tokenCanceled;
            OuterCanceled = outerCanceled;
            Body = body;
        }

        /// <summary>释放链路令牌源（取消待决定时器；不请求取消）。调用方须在 await/排空 <see cref="Body"/> 后 Dispose。</summary>
        public void Dispose() => _linked.Dispose();
    }

    /// <summary>启动一次有界执行（body 返回结果）：body 在 linked-CTS（CancelAfter timeout）下运行，与定时器竞速，返回结果（不 await body）。</summary>
    public static async Task<Race<T>> StartAsync<T>(
        Func<CancellationToken, Task<T>> body, TimeSpan timeout, CancellationToken outer)
    {
        ArgumentNullException.ThrowIfNull(body);
        var linked = CancellationTokenSource.CreateLinkedTokenSource(outer);
        linked.CancelAfter(timeout);
        var work = body(linked.Token);
        var timer = Task.Delay(Timeout.InfiniteTimeSpan, linked.Token);
        var winner = await Task.WhenAny(work, timer).ConfigureAwait(false);
        var completed = ReferenceEquals(winner, work);
        var tokenCanceled = linked.IsCancellationRequested;
        var outerCanceled = outer.IsCancellationRequested;
        return new Race<T>(linked, completed, tokenCanceled, outerCanceled, work);
    }

    /// <summary>启动一次有界执行（void body）：await body 后视作完成；调用方忽略 <see cref="Race{T}.Body"/> 的值（true）。</summary>
    public static Task<Race<bool>> StartAsync(
        Func<CancellationToken, Task> body, TimeSpan timeout, CancellationToken outer)
        => StartAsync<bool>(async ct => { await body(ct).ConfigureAwait(false); return true; }, timeout, outer);
}