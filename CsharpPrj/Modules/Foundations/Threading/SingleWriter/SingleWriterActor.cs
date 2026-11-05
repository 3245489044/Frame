using System.Threading.Channels;

namespace Modules.Threading;

/// <summary>
/// 单写 actor 线程框架（存储无关）：为「单个文件 / 单写资源」提供正确的并发底座。
/// 核心：一个 <b>专用后台 worker 线程</b> 独占一份资源（如 SQLite 单写连接），其它线程只做<b>提交工作项</b>、
/// 绝不触碰资源；worker 在自身线程上 FIFO 串行执行，资源永不离线程。
/// 由此从机制上消除「多执行流抢同一物理单写资源」的竞争/饿死/门超时，以及「AsyncLocal 跨 await 失效」类问题。
/// <para><b>防死锁设计（构造性）</b>：本框架的<b>工作体一律为同步体</b>（<see cref="Execute"/> 或
/// <see cref="ExecuteAsync{T}"/> 入队的 <c>Func&lt;TResource,&gt;</c>），在 worker 线程上同步跑完、绝不 await 跳线程。
/// 于是：</para>
/// (1) 重入 = <b>线程身份</b>：调用方即 worker → <b>内联同步执行</b>，同一线程天然安全、仍串行；
///     worker 线程绝不因 await 把续延抛到线程池 → 不可能出现「worker 等队列、队列等 worker」的互等死锁。
/// (2) 异步 <see cref="ExecuteAsync{T}"/> 只是<b>调用方侧</b>的"入队 + 等待完成"，入队的工作体仍是同步体。
/// 由此把旧版「worker 阻塞等异步体完成」的死锁温床从构造上移除。
/// <para>正确性边界（使用方必须遵守）：</para>
/// (1) 每个单写资源（文件）至多一个 actor；不同资源各自独立 actor，互不排队。
/// (2) worker 工作体不得<b>同步阻塞等待另一线程对该 actor 的提交</b>（worker 阻塞 → 写队列停摆 → 对端等 worker → 死锁）；
///     如需本 actor 的嵌套写，调用方已是 worker 线程会自动内联执行，安全。
/// (3) 取消令牌只作用于<b>调用方等待</b>，不中断已在 worker 上执行的工作（单写者跑完为止）。
/// </summary>
/// <typeparam name="TResource">被独占的资源类型（如 <c>SQLiteConnection</c>）；仅 worker 线程持有并访问。</typeparam>
public sealed class SingleWriterActor<TResource> : IDisposable where TResource : class
{
    /// <summary>统一工作项：worker 取出后在其线程<b>同步</b>执行并完成对应等待。</summary>
    private interface IRequest
    {
        void Run(TResource resource);
    }

    private sealed class Request<T> : IRequest
    {
        private readonly Func<TResource, T> _body;
        private readonly TaskCompletionSource<T> _tcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly CancellationToken _startCancel;

        public Request(Func<TResource, T> body, CancellationToken startCancel)
        {
            _body = body;
            _startCancel = startCancel;
        }

        public Task<T> Completion => _tcs.Task;

        /// <summary>worker 上同步执行（无 await、不跳线程）。</summary>
        public void Run(TResource resource)
        {
            if (_startCancel.IsCancellationRequested) { _tcs.TrySetCanceled(); return; }
            try { _tcs.SetResult(_body(resource)); }
            catch (OperationCanceledException ex) { _tcs.TrySetCanceled(ex.CancellationToken); }
            catch (Exception ex) { _tcs.TrySetException(ex); }
        }
    }

    private readonly Func<TResource> _create;
    private readonly Action<TResource>? _dispose;
    private readonly Channel<IRequest> _queue;
    private readonly Thread _worker;
    private readonly string _name;
    private readonly object _sync = new();

    private TResource? _resource;
    private bool _started;
    private bool _stopped;

    /// <param name="create">在 worker 线程上创建独占资源（仅一次）。</param>
    /// <param name="name">worker 线程名 / 诊断标识（如 <c>agent.db-single-writer</c>）。</param>
    /// <param name="dispose">停止时在 worker 线程上释放资源（可选）。</param>
    /// <param name="capacity">有界队列容量；0 表无界（默认）。有界时提交方可能短暂等待空位。</param>
    public SingleWriterActor(Func<TResource> create, string name, Action<TResource>? dispose = null, int capacity = 0)
    {
        _create = create ?? throw new ArgumentNullException(nameof(create));
        _dispose = dispose;
        _name = string.IsNullOrWhiteSpace(name) ? "single-writer" : name;
        _queue = capacity > 0
            ? Channel.CreateBounded<IRequest>(new BoundedChannelOptions(capacity) { FullMode = BoundedChannelFullMode.Wait })
            : Channel.CreateUnbounded<IRequest>(new UnboundedChannelOptions { SingleReader = true, SingleWriter = false, AllowSynchronousContinuations = false });
        _worker = new Thread(WorkerLoop) { Name = _name, IsBackground = true };
    }

    /// <summary>是否调用方即本 actor 的 worker 线程（据此自动内联重入）。</summary>
    public bool IsCurrentWorker => ReferenceEquals(Thread.CurrentThread, _worker);

    /// <summary>启动 worker（幂等）：在 worker 线程创建资源并开始 FIFO 消费。停用后再次 Start 为 no-op。</summary>
    public void Start()
    {
        lock (_sync)
        {
            if (_started || _stopped) return;
            _started = true;
        }
        _worker.Start();
    }

    /// <summary>同步提交执行：阻塞调用方直至 worker 完成。worker 线程内联直接执行（不阻塞、不重入队列）。</summary>
    public T Execute<T>(Func<TResource, T> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        ct.ThrowIfCancellationRequested();
        if (IsCurrentWorker) { EnsureResource(); return work(Resource); }
        return EnqueueAndWait(work, ct);
    }

    /// <summary>同步提交执行（void 体）。</summary>
    public void Execute(Action<TResource> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        Execute<byte>(r => { work(r); return 0; }, ct);
    }

    /// <summary>异步提交执行：入队同步体并 await 完成（调用方不阻塞；入队的工作体仍在 worker 上同步跑）。</summary>
    public Task<T> ExecuteAsync<T>(Func<TResource, T> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        if (IsCurrentWorker)
        {
            try { EnsureResource(); ct.ThrowIfCancellationRequested(); return Task.FromResult(work(Resource)); }
            catch (Exception ex) { return Task.FromException<T>(ex); }
        }
        return EnqueueAndAwait(work, ct);
    }

    /// <summary>异步提交执行（void 体）。</summary>
    public Task ExecuteAsync(Action<TResource> work, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(work);
        return ExecuteAsync(r => { work(r); return 0; }, ct);
    }

    /// <summary>当前独占资源；仅应在 worker 线程（含内联重入）内访问，跨线程访问抛错。</summary>
    public TResource Resource
    {
        get
        {
            if (!IsCurrentWorker) throw new InvalidOperationException($"SingleWriterActor '{_name}': 资源只能由 worker 线程访问。");
            EnsureResource();
            return _resource!;
        }
    }

    private T EnqueueAndWait<T>(Func<TResource, T> body, CancellationToken ct)
    {
        var req = new Request<T>(body, ct);
        if (!TryEnqueue(req, out var fault)) throw fault ?? new InvalidOperationException($"SingleWriterActor '{_name}' 未运行。");
        try { return req.Completion.GetAwaiter().GetResult(); }
        catch (Exception) when (ct.IsCancellationRequested && !req.Completion.IsCompleted) { throw new OperationCanceledException(ct); }
    }

    private async Task<T> EnqueueAndAwait<T>(Func<TResource, T> body, CancellationToken ct)
    {
        var req = new Request<T>(body, ct);
        if (!TryEnqueue(req, out var fault))
            throw fault ?? new InvalidOperationException($"SingleWriterActor '{_name}' 未运行。");
        if (ct.CanBeCanceled)
        {
            try { return await req.Completion.WaitAsync(ct).ConfigureAwait(false); }
            catch (OperationCanceledException) when (!req.Completion.IsCompleted) { throw; }
        }
        return await req.Completion.ConfigureAwait(false);
    }

    private bool TryEnqueue(IRequest request, out Exception? fault)
    {
        lock (_sync)
        {
            if (!_started || _stopped)
            {
                fault = new InvalidOperationException($"SingleWriterActor '{_name}' 未启动或已停止。");
                return false;
            }
        }
        fault = null;
        if (!_queue.Writer.TryWrite(request))
        {
            fault = new InvalidOperationException($"SingleWriterActor '{_name}' 队列已满。");
            return false;
        }
        return true;
    }

    private void EnsureResource()
    {
        if (_resource is null) throw new InvalidOperationException($"SingleWriterActor '{_name}' 资源尚未就绪（可能未 Start 或启动失败）。");
    }

    private void WorkerLoop()
    {
        try
        {
            _resource = _create();
            var reader = _queue.Reader;
            while (reader.WaitToReadAsync().AsTask().GetAwaiter().GetResult())
            {
                while (reader.TryRead(out var item))
                {
                    if (IsStopRequested) { DrainQuiet(reader); return; }
                    item.Run(_resource);
                }
            }
        }
        catch
        {
            FailOutstanding();
        }
        finally
        {
            lock (_sync)
            {
                if (_resource is not null)
                {
                    var r = _resource;
                    _resource = default;
                    try { _dispose?.Invoke(r); } catch { /* 释放失败静默，不向上抛 */ }
                }
            }
        }
    }

    private bool IsStopRequested
    {
        get { lock (_sync) { return _stopped; } }
    }

    private void DrainQuiet(ChannelReader<IRequest> reader)
    {
        // 停止期已入队但未执行的工作项：不执行（防半写），由各请求的调用方经取消/超时收尾。
        while (reader.TryRead(out _)) { }
    }

    /// <summary>worker 异常崩出（如 create 抛错）：尽量让在途请求不被永久挂起。
    /// IRequest 无统一失败入口，此处尽力完成队列（多数请求仍由其调用方经取消令牌收尾）。</summary>
    private void FailOutstanding()
    {
        try { while (_queue.Reader.TryRead(out _)) { } }
        catch { /* 静默 */ }
    }

    /// <summary>优雅停止：拒绝新提交；已入队未执行项丢弃（防半写）。在途工作项跑完后释放资源并结束 worker 线程。</summary>
    public void Stop()
    {
        lock (_sync)
        {
            if (_stopped) return;
            _stopped = true;
        }
        // 完成写入端以唤醒 worker（在途跑完后见 IsStopRequested → DrainQuiet → 退出）；新提交已被 _stopped 拒绝。
        try { _queue.Writer.TryComplete(); }
        catch { /* 已 Complete 可忽略 */ }
    }

    public void Dispose() => Stop();
}

