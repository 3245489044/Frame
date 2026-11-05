using System;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Modules.Threading
{
    /// <summary>
    /// 串行派发器：基于 <see cref="System.Threading.Channels.Channel{T}"/> 的单后台消费者，同一投递源交付**保序**。
    /// 用于 StateSubscriptionHub 等广播与写线程解耦（StateBus 治理 P2/P3）——写线程只 <see cref="Post"/> 入队即返回，
    /// 订阅 handler 在单一后台消费者上串行执行，写线程不被订阅链阻塞。
    /// <para>
    /// 相比 BlockingCollection 的收益（架构优化）：
    /// - 可选**有界容量 + 背压**（BoundedChannelFullMode.Wait：<see cref="PostAsync"/> 有界等待；Drop/DiscardWrite 丢旧/丢新）；
    /// - 无界投递 <see cref="Post"/> 永不阻塞写线程；通道满/已释放时退化为内联执行（不丢投递）；
    /// - 原生 async 消费（await foreach），天然支持取消与优雅排空。
    /// </para>
    /// 单条 handler 异常隔离（不影响后续）。
    /// </summary>
    public sealed class SerialDispatcher : IThreadDispatcher, IDisposable, IAsyncDisposable
    {
        private readonly Channel<Action> _channel;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _worker;
        private int _disposed;

        /// <param name="capacity">有界容量；null/&lt;=0 = 无界（Post 永不阻塞）。</param>
        /// <param name="fullMode">有界满时的模式（默认 Wait：PostAsync 背压等待；Post 满时退化为内联）。</param>
        public SerialDispatcher(int? capacity = null, string? name = null,
            BoundedChannelFullMode fullMode = BoundedChannelFullMode.Wait)
        {
            if (capacity is > 0)
            {
                _channel = Channel.CreateBounded<Action>(new BoundedChannelOptions(capacity.Value)
                {
                    FullMode = fullMode,
                    SingleReader = true,
                    SingleWriter = false,
                    AllowSynchronousContinuations = false
                });
            }
            else
            {
                _channel = Channel.CreateUnbounded<Action>(new UnboundedChannelOptions { SingleReader = true });
            }

            _worker = Task.Run(() => ConsumeAsync(_cts.Token));
            _ = string.IsNullOrWhiteSpace(name); // name 保留给诊断（如需命名消费者可扩展）
        }

        /// <summary>投递一次工作（入队即返回，不阻塞调用线程）。已释放/有界满 → 退化为内联执行，保证不丢。</summary>
        public void Post(Action delivery)
        {
            ArgumentNullException.ThrowIfNull(delivery);
            if (Volatile.Read(ref _disposed) != 0 || !_channel.Writer.TryWrite(delivery))
                delivery();
        }

        /// <summary>异步投递：无界即时入队；有界 Wait 模式会背压等待写者放行（慢消费者限流写者）。</summary>
        public async ValueTask PostAsync(Action delivery)
        {
            ArgumentNullException.ThrowIfNull(delivery);
            if (Volatile.Read(ref _disposed) != 0)
            {
                delivery();
                return;
            }
            await _channel.Writer.WriteAsync(delivery).ConfigureAwait(false);
        }

        private async Task ConsumeAsync(CancellationToken ct)
        {
            try
            {
                await foreach (var delivery in _channel.Reader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    try { delivery(); }
                    catch { /* 单条投递异常隔离：不影响其余投递 */ }
                }
            }
            catch (OperationCanceledException)
            {
                // 优雅关闭
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _cts.Cancel();
            _channel.Writer.TryComplete();
        }

        public async ValueTask DisposeAsync()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            _cts.Cancel();
            _channel.Writer.TryComplete();
            try { await _worker.ConfigureAwait(false); } catch { /* 消费者异常已隔离 */ }
        }
    }
}
