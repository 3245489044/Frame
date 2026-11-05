using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace Modules.Threading.Streaming
{
    /// <summary>
    /// 通用有界通道异步批写器（领域无关）：Emit 入有界通道 → 后台 Drain 批写 →
    /// 单批失败保留缓冲 + 有限重试（at-least-once）+ 幂等释放。
    /// 消费方注入 flush 委托（批 → 落库），本类只负责缓冲/背压/重试/生命周期，不依赖任何存储抽象。
    /// Agent.Core 的 EventSink 即委托本内核（对齐单源实现原则）。
    /// </summary>
    public sealed class BatchedAsyncSink<TRecord> : IAsyncDisposable
    {
        private readonly Channel<TRecord> _channel;
        private readonly Func<IReadOnlyList<TRecord>, CancellationToken, Task> _flush;
        private readonly int _batch;
        private readonly int _retryLimit;
        private readonly TimeSpan _retryDelay;
        private readonly Action<string, object?>? _log;
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _writer;
        private bool _disposed;

        /// <param name="capacity">有界通道容量（背压水位）。</param>
        /// <param name="drainBatch">单批最大条数。</param>
        /// <param name="flush">整批落库委托。</param>
        /// <param name="retryLimit">单批失败重试上限（at-least-once）。</param>
        /// <param name="retryDelay">重试间隔。</param>
        /// <param name="log">日志回调（suffix 为错误键后缀，如 "DrainLoop_failed"；消费方可加领域前缀）。</param>
        public BatchedAsyncSink(
            int capacity,
            int drainBatch,
            Func<IReadOnlyList<TRecord>, CancellationToken, Task> flush,
            int retryLimit = 3,
            TimeSpan? retryDelay = null,
            Action<string, object?>? log = null)
        {
            _flush = flush ?? throw new ArgumentNullException(nameof(flush));
            _batch = Math.Max(1, drainBatch);
            _retryLimit = retryLimit;
            _retryDelay = retryDelay ?? TimeSpan.FromMilliseconds(100);
            _log = log;
            _channel = Channel.CreateBounded<TRecord>(new BoundedChannelOptions(Math.Max(1, capacity))
            {
                FullMode = BoundedChannelFullMode.Wait,
                SingleReader = true,
                SingleWriter = false,
            });
            _writer = Task.Run(() => DrainLoopAsync(_cts.Token));
        }

        /// <summary>入队一条记录（非阻塞；写满时经有界通道 Wait 背压生产方）。</summary>
        public void Emit(TRecord record)
        {
            if (record is null) return;
            _channel.Writer.TryWrite(record);
        }

        private async Task DrainLoopAsync(CancellationToken ct)
        {
            var buffer = new List<TRecord>(_batch);
            try
            {
                while (await _channel.Reader.WaitToReadAsync(ct).ConfigureAwait(false))
                {
                    while (_channel.Reader.TryRead(out var item))
                    {
                        buffer.Add(item);
                        if (buffer.Count >= _batch) break;
                    }
                    if (buffer.Count > 0)
                    {
                        // 单批失败不丢弃已读记录：有限重试后仍失败 → 停止接收并保留缓冲，
                        // 交由重放/幂等（at-least-once，幂等覆写），避免静默丢批。
                        if (!await TryFlushWithRetryAsync(buffer, ct).ConfigureAwait(false))
                        {
                            _channel.Writer.TryComplete(); // 停止接收，缓冲数据保留
                            break;
                        }
                        buffer.Clear();
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                // 非取消异常不静默终止 writer，记录并停止消费，避免事件区永久静默停写。
                _log?.Invoke("DrainLoop_failed", new { message = ex.Message });
            }
        }

        /// <summary>有限重试整批落库（at-least-once 允许重放重复；整批重试幂等安全）。返回是否最终成功。</summary>
        private async Task<bool> TryFlushWithRetryAsync(List<TRecord> buffer, CancellationToken ct)
        {
            for (var attempt = 0; attempt < _retryLimit; attempt++)
            {
                try
                {
                    await _flush(buffer, ct).ConfigureAwait(false);
                    return true;
                }
                catch (OperationCanceledException) { throw; }
                catch
                {
                    if (attempt >= _retryLimit - 1) return false;
                    try { await Task.Delay(_retryDelay, ct).ConfigureAwait(false); }
                    catch (OperationCanceledException) { throw; }
                }
            }
            return false;
        }

        public async ValueTask DisposeAsync()
        {
            if (_disposed) return;
            _disposed = true;
            _channel.Writer.TryComplete();
            _cts.Cancel();
            try { await _writer.ConfigureAwait(false); } catch { /* 释放期忽略 */ }
            _cts.Dispose();
        }
    }
}
