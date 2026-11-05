using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Modules.Threading.Streaming
{
    /// <summary>
    /// 通用有界队列消费者（领域无关）：订阅方 Enqueue → 有界队列 → 有界并发消费者
    /// 按 key 幂等去重逐项处理（at-least-once）。已处理集合由调用方在处理器 finally 中
    /// <see cref="MarkProcessed"/> 登记；本类仅在消费循环/单次处理做 <see cref="IsProcessed"/> 去重。
    /// Agent.Core 的 ReactiveTrigger 消费骨架即委托本内核（对齐单源实现原则）。
    /// </summary>
    public sealed class DedupQueueConsumer<TItem> : IAsyncDisposable
    {
        private readonly Channel<TItem> _channel;
        private readonly SemaphoreSlim _gate;
        private readonly Func<TItem, string> _keySelector;
        private readonly Func<TItem, CancellationToken, Task> _processor;
        private readonly HashSet<string> _processed = new(StringComparer.Ordinal);
        private readonly object _lock = new();
        private CancellationTokenSource? _cts;
        private Task? _consumer;
        private bool _started;

        /// <param name="queueCapacity">有界队列容量（背压水位）。</param>
        /// <param name="maxConcurrency">并发处理上限。</param>
        /// <param name="keySelector">幂等去重键（如 InputKey/Id）。</param>
        /// <param name="processor">单条处理委托（含失败保留/重放语义由调用方实现）。</param>
        public DedupQueueConsumer(
            int queueCapacity,
            int maxConcurrency,
            Func<TItem, string> keySelector,
            Func<TItem, CancellationToken, Task> processor)
        {
            _keySelector = keySelector ?? throw new ArgumentNullException(nameof(keySelector));
            _processor = processor ?? throw new ArgumentNullException(nameof(processor));
            _gate = new SemaphoreSlim(Math.Max(1, maxConcurrency));
            _channel = Channel.CreateBounded<TItem>(new BoundedChannelOptions(Math.Max(1, queueCapacity))
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });
        }

        /// <summary>是否已处理（幂等去重判定，线程安全）。</summary>
        public bool IsProcessed(TItem item)
        {
            lock (_lock) return _processed.Contains(_keySelector(item));
        }

        /// <summary>调用方在处理器 finally 中登记已处理（含失败路径，保证至少尝试一次语义）。</summary>
        public void MarkProcessed(TItem item)
        {
            lock (_lock) _processed.Add(_keySelector(item));
        }

        /// <summary>非阻塞入队（背压经有界通道体现；写满时阻塞生产方）。</summary>
        public void Enqueue(TItem item)
        {
            if (item is null) return;
            _channel.Writer.TryWrite(item);
        }

        /// <summary>启动后台消费循环。</summary>
        public void Start(CancellationToken ct = default)
        {
            if (_started) return;
            _started = true;
            _cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            _consumer = Task.Run(() => ConsumeLoopAsync(_cts.Token));
        }

        /// <summary>停止并等待消费循环退出。</summary>
        public async Task StopAsync()
        {
            if (!_started) return;
            _started = false;
            var cts = _cts;
            _cts = null;
            _channel.Writer.TryComplete();
            if (cts is not null)
            {
                cts.Cancel();
                try { if (_consumer is not null) await _consumer.ConfigureAwait(false); } catch { }
                cts.Dispose();
            }
        }

        /// <summary>单次处理（经并发门 + 幂等去重），供回填/手动驱动与消费循环共用。返回是否实际处理。</summary>
        public async Task<bool> TryProcessAsync(TItem item, CancellationToken ct)
        {
            if (IsProcessed(item)) return false;
            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try { await _processor(item, ct).ConfigureAwait(false); return true; }
            finally { _gate.Release(); }
        }

        private async Task ConsumeLoopAsync(CancellationToken ct)
        {
            try
            {
                while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
                {
                    while (_channel.Reader.TryRead(out var item))
                    {
                        await TryProcessAsync(item, ct).ConfigureAwait(false);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch { /* 单轮失败不中断消费 */ }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
            _gate.Dispose();
        }
    }
}
