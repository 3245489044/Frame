using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Threading.Streaming;

/// <summary>Outbox 日志事件种类（日志插槽区分）；Agent 侧按种类回填对应代理日志文案。</summary>
public enum OutboxEventKind
{
    /// <summary>单条落库超高水位（后台写者长持写面）。</summary>
    FlushSlow,
    /// <summary>单条落库超时（死信归类，放弃该条）。</summary>
    ItemTimeout,
    /// <summary>单条落库失败（死信归类，不阻塞后续）。</summary>
    ItemFailed,
}

/// <summary>Outbox 日志上下文 —— 通用日志插槽载荷；TWork 对框架不透明，交 Agent 侧按需提取（如 node 键）。</summary>
public sealed record OutboxLogContext<TWork>(
    TWork Item,
    long ElapsedMs = 0,
    long TimeoutMs = 0,
    string? ErrorType = null,
    string? Error = null,
    string Owner = "",
    int Queue = 0);

/// <summary>Outbox 端口：追加工作项（内存 O(1)，不阻塞调用方；返回前不执行 handler）。进程内单 writer 后台消费。</summary>
public interface IOutbox<in TWork>
{
    /// <summary>追加一条工作项（内存 O(1)，不阻塞；返回前不落库）。</summary>
    void Append(TWork work);
}

/// <summary>
/// Outbox 后台 writer —— 独立单 worker 消费 <typeparamref name="TWork"/>：
/// 逐条经注入的落库处理器 <paramref name="_handler"/> 落库，单条有界超时 + 失败死信归类，绝不阻塞任意调用方。
/// 取消令牌贯穿；停止时做最终排空。日志经注入的 <see cref="Action{T1,T2}"/> 插槽触达（框架零 Agent 依赖）。
/// </summary>
public sealed class OutboxWriter<TWork> : IOutbox<TWork>, IAsyncDisposable
{
    private readonly ConcurrentQueue<TWork> _queue = new();
    private readonly SemaphoreSlim _signal = new(0, int.MaxValue);
    private readonly Func<TWork, CancellationToken, Task> _handler;
    private readonly Action<OutboxEventKind, OutboxLogContext<TWork>>? _log;
    private readonly TimeSpan _perItemTimeout;
    private readonly CancellationTokenSource _cts = new();
    private readonly Task _drainTask;
    private int _disposed;

    // Stage 3 长事务治理：单次信号后最多排空的有界批量数。避免一次唤醒把巨量积压一口气经单写通道排空、
    // 长时间独占写面（如 agent.db）而饿死读者；每批量间让出通道供写者/读者交错，余队经重新置位继续。
    private const int MaxBurstPerSignal = 32;

    // S3-2 写者 owner 自报告（只读取证，非 watchdog 槽）：写者与主循环读共享介质，是「读被写锁堵住」的候选持锁方。
    private readonly string _ownerLabel = $"outbox-writer:{Environment.ProcessId}";
    private int _flushing;        // 1 = 正在落一条（持锁窗口；单 worker 故 0/1）
    private long _processedCount; // 已成功落库条数
    private int _activeThreadId;  // 当前处理线程 id（持锁写者线程，取证用）
    /// <summary>供取证装配的只读证据源（名 "outbox"；未装配 assembler 时零调用零开销）。</summary>
    public Modules.Threading.IHangEvidenceSource EvidenceSource { get; }

    /// <param name="handler">落库处理器；异常由 writer 归类，不上抛。</param>
    /// <param name="perItemTimeout">单条落库有界超时；卡超即放弃该条（死信归类），不阻塞后续条目。</param>
    /// <param name="log">日志插槽（Agent 侧回填代理日志门面）；null = 静默。</param>
    public OutboxWriter(
        Func<TWork, CancellationToken, Task> handler,
        TimeSpan perItemTimeout,
        Action<OutboxEventKind, OutboxLogContext<TWork>>? log = null)
    {
        _handler = handler ?? throw new ArgumentNullException(nameof(handler));
        _perItemTimeout = perItemTimeout > TimeSpan.Zero ? perItemTimeout : TimeSpan.FromSeconds(5);
        _log = log;
        EvidenceSource = new OutboxEvidenceSource(this);
        // 后台持久化 worker 不继承外层 run 上下文（切 ExecutionContext 流），防 run 作用域内装配时泄漏上下文到持久化线程。
        using (ExecutionContext.SuppressFlow())
        {
            _drainTask = Task.Factory.StartNew(DrainLoop, CancellationToken.None,
                TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }
    }

    /// <inheritdoc />
    public void Append(TWork work)
    {
        if (work is null || _cts.IsCancellationRequested) return;
        _queue.Enqueue(work);
        Signal();
    }

    /// <summary>后台消费循环：等待信号 → 有界批量落库（每次信号最多 <see cref="MaxBurstPerSignal"/> 条）；取消令牌触发停止并做最终排空（停止前不丢已入队条目）。</summary>
    private async Task DrainLoop()
    {
        var ct = _cts.Token;
        while (true)
        {
            try { await _signal.WaitAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) { break; }

            var drained = 0;
            while (drained < MaxBurstPerSignal && _queue.TryDequeue(out var work))
            {
                await FlushOneAsync(work, ct).ConfigureAwait(false);
                drained++;
            }
            if (_queue.Count > 0)
                Signal();
        }

        // 最终排空（停止信号后仍把已入队条目落完，避免进程退出丢数据）。
        while (_queue.TryDequeue(out var work))
            await FlushOneAsync(work, CancellationToken.None).ConfigureAwait(false);
    }

    /// <summary>单条落库（写者 owner 自报告包裹）：进入前登记持锁写者线程/在途，退出后清在途并累计处理数（供 S3-2 取证）。</summary>
    private const int HighWaterFlushMs = 3000;

    private async Task FlushOneAsync(TWork work, CancellationToken ct)
    {
        Interlocked.Exchange(ref _activeThreadId, Environment.CurrentManagedThreadId);
        Interlocked.Exchange(ref _flushing, 1);
        var sw = Stopwatch.StartNew();
        try
        {
            await HandleAsync(work, ct).ConfigureAwait(false);
        }
        finally
        {
            sw.Stop();
            if (sw.ElapsedMilliseconds >= HighWaterFlushMs)
            {
                _log?.Invoke(OutboxEventKind.FlushSlow,
                    new OutboxLogContext<TWork>(work, ElapsedMs: sw.ElapsedMilliseconds, Owner: _ownerLabel, Queue: _queue.Count));
            }
            Interlocked.Exchange(ref _flushing, 0);
            Interlocked.Increment(ref _processedCount);
        }
    }

    /// <summary>单条落库：linked-CTS 有界超时；失败/超时归类日志并放弃该条（死信），不无限重试阻塞后续。</summary>
    private async Task HandleAsync(TWork work, CancellationToken outerCt)
    {
        using var cts = CancellationTokenSource.CreateLinkedTokenSource(outerCt);
        cts.CancelAfter(_perItemTimeout);
        try
        {
            await _handler(work, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _log?.Invoke(OutboxEventKind.ItemTimeout,
                new OutboxLogContext<TWork>(work, TimeoutMs: (long)_perItemTimeout.TotalMilliseconds, Owner: _ownerLabel, Queue: _queue.Count));
        }
        catch (Exception ex)
        {
            // 落库失败死信归类（对齐「持久化失败不翻转业务」语义）；单条失败不阻塞后续条目。
            _log?.Invoke(OutboxEventKind.ItemFailed,
                new OutboxLogContext<TWork>(work, ErrorType: ex.GetType().Name, Error: ex.Message, Owner: _ownerLabel, Queue: _queue.Count));
        }
    }

    private void Signal()
    {
        try { _signal.Release(); }
        catch (SemaphoreFullException) { /* 信号已满，队列非空，循环自会消费 */ }
    }

    public async ValueTask DisposeAsync()
    {
        if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
        _cts.Cancel();
        Signal();
        try { await _drainTask.ConfigureAwait(false); }
        catch { /* 停止阶段异常可忽略 */ }
        _cts.Dispose();
        _signal.Dispose();
    }

    /// <summary>取证瞬间的写者快照（可序列化值；owner=写者标识，供 A 类判定持锁方）。</summary>
    private object? CaptureSnapshot() => new Dictionary<string, object?>
    {
        ["owner"] = _ownerLabel,
        ["threadId"] = Volatile.Read(ref _activeThreadId),
        ["flushing"] = Volatile.Read(ref _flushing) != 0,
        ["queue"] = _queue.Count,
        ["processed"] = Interlocked.Read(ref _processedCount),
        ["perItemTimeoutMs"] = (long)_perItemTimeout.TotalMilliseconds,
    };

    /// <summary>写者证据源（Modules.Threading.IHangEvidenceSource 契约；叶子内核只读快照，无业务反向依赖）。</summary>
    private sealed class OutboxEvidenceSource : Modules.Threading.IHangEvidenceSource
    {
        private readonly OutboxWriter<TWork> _owner;
        public OutboxEvidenceSource(OutboxWriter<TWork> owner) => _owner = owner;
        public string Name => "outbox";
        public object? Capture() => _owner.CaptureSnapshot();
    }
}