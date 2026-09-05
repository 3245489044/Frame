
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BasicMethod.Queue
{
    public static class QueueExtensions
    {

        public static IQueue<TMessage> WithRetry<TMessage>(
            this IQueue<TMessage> queue,
            int maxRetries = 3,
            TimeSpan? retryDelay = null)
        {
            ArgumentNullException.ThrowIfNull(queue);
            return new RetryQueueDecorator<TMessage>(queue, maxRetries, retryDelay ?? TimeSpan.FromSeconds(1));
        }


        public static IQueue<TMessage> WithDeadLetter<TMessage>(
            this IQueue<TMessage> queue,
            IDeadLetterQueue<TMessage> deadLetterQueue)
        {
            ArgumentNullException.ThrowIfNull(queue);
            ArgumentNullException.ThrowIfNull(deadLetterQueue);
            return new DeadLetterQueueDecorator<TMessage>(queue, deadLetterQueue);
        }


        public static IQueue<TMessage> WithMetrics<TMessage>(this IQueue<TMessage> queue)
        {
            ArgumentNullException.ThrowIfNull(queue);
            return new MetricsQueueDecorator<TMessage>(queue);
        }


        public static IQueue<TMessage> Compose<TMessage>(params IQueue<TMessage>[] queues)
        {
            ArgumentNullException.ThrowIfNull(queues);
            return new CompositeQueue<TMessage>(queues);
        }


        private class RetryQueueDecorator<TMessage> : QueueDecoratorBase<TMessage>
        {
            private readonly int _maxRetries;
            private readonly TimeSpan _retryDelay;
            private Func<TMessage, CancellationToken, Task<QueueResult>>? _handler;

            public RetryQueueDecorator(IQueue<TMessage> inner, int maxRetries, TimeSpan retryDelay)
                : base(inner, $"{inner.Name}.retry", $"带重试的队列（最多 {maxRetries} 次）")
            {
                _maxRetries = maxRetries;
                _retryDelay = retryDelay;
            }

            public override Task StartConsumingAsync(
                Func<TMessage, CancellationToken, Task<QueueResult>> handler,
                CancellationToken cancellationToken = default)
            {
                ArgumentNullException.ThrowIfNull(handler);
                _handler = handler;
                return base.StartConsumingAsync(handler, cancellationToken);
            }

            protected override async Task ConsumeLoopAsync(CancellationToken cancellationToken)
            {
                while (!cancellationToken.IsCancellationRequested)
                {
                    try
                    {
                        var (success, message) = await TryDequeueAsync(cancellationToken);
                        if (success && message != null && _handler != null)
                        {
                            bool processed = false;
                            for (int attempt = 1; attempt <= _maxRetries && !processed; attempt++)
                            {
                                try
                                {
                                    if (attempt > 1)
                                        await Task.Delay(_retryDelay, cancellationToken);

                                    var result = await _handler(message, cancellationToken);
                                    if (result.IsSuccess)
                                    {
                                        processed = true;
                                    }
                                    else
                                    {
                                        if (attempt >= _maxRetries)
                                        {
                                            await _inner.EnqueueAsync(message, cancellationToken);
                                        }
                                    }
                                }
                                catch (OperationCanceledException)
                                {
                                    return;
                                }
                                catch
                                {
                                    if (attempt >= _maxRetries)
                                    {
                                        await _inner.EnqueueAsync(message, cancellationToken);
                                    }
                                }
                            }
                        }
                        else if (!success)
                        {
                            await Task.Delay(10, cancellationToken);
                        }
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }

        private class DeadLetterQueueDecorator<TMessage> : QueueDecoratorBase<TMessage>
        {
            private readonly IDeadLetterQueue<TMessage> _deadLetter;

            public DeadLetterQueueDecorator(IQueue<TMessage> inner, IDeadLetterQueue<TMessage> deadLetter)
                : base(inner, $"{inner.Name}.dlq", $"带死信的队列")
            {
                _deadLetter = deadLetter;
            }
        }

        private class MetricsQueueDecorator<TMessage> : QueueDecoratorBase<TMessage>, IQueueMonitor<TMessage>
        {
            private long _totalEnqueued;
            private long _totalDequeued;
            private long _totalProcessed;
            private long _totalFailed;

            public long TotalEnqueued => _totalEnqueued;
            public long TotalDequeued => _totalDequeued;
            public long TotalProcessed => _totalProcessed;
            public long TotalFailed => _totalFailed;

            public MetricsQueueDecorator(IQueue<TMessage> inner)
                : base(inner, $"{inner.Name}.metrics", $"带统计的队列")
            {
            }

            public override async Task<QueueResult> EnqueueAsync(TMessage message, CancellationToken cancellationToken = default)
            {
                var result = await base.EnqueueAsync(message, cancellationToken);
                if (result.IsSuccess) Interlocked.Increment(ref _totalEnqueued);
                return result;
            }

            public override async Task<(bool Success, TMessage? Message)> TryDequeueAsync(CancellationToken cancellationToken = default)
            {
                var result = await base.TryDequeueAsync(cancellationToken);
                if (result.Success) Interlocked.Increment(ref _totalDequeued);
                return result;
            }

            internal void RecordSuccess() => Interlocked.Increment(ref _totalProcessed);

            internal void RecordFailure() => Interlocked.Increment(ref _totalFailed);

            public QueueStats GetStats() => new(
                PendingCount: Count,
                ProcessingCount: 0,
                CompletedCount: (int)_totalProcessed,
                FailedCount: (int)_totalFailed,
                DeadLetterCount: 0,
                AverageWaitTime: TimeSpan.Zero,
                AverageProcessTime: TimeSpan.Zero
            );
        }

        private class CompositeQueue<TMessage> : QueueBase<TMessage>
        {
            private readonly IReadOnlyList<IQueue<TMessage>> _queues;

            public override int Count
            {
                get
                {
                    int total = 0;
                    foreach (var q in _queues) total += q.Count;
                    return total;
                }
            }

            public CompositeQueue(IReadOnlyList<IQueue<TMessage>> queues)
                : base("composite", $"复合队列（{queues.Count} 个子队列）")
            {
                _queues = queues;
            }

            public override async Task<QueueResult> EnqueueAsync(TMessage message, CancellationToken cancellationToken = default)
            {
                foreach (var queue in _queues)
                {
                    var result = await queue.EnqueueAsync(message, cancellationToken);
                    if (!result.IsSuccess)
                        return QueueResult.Fail($"子队列 '{queue.Name}' 入队失败: {result.Message}");
                }
                return QueueResult.Ok($"消息已入队到 {_queues.Count} 个子队列");
            }

            public override Task<(bool Success, TMessage? Message)> TryDequeueAsync(CancellationToken cancellationToken = default)
            {
                throw new NotSupportedException("复合队列不支持出队操作");
            }

            public override void Clear()
            {
                foreach (var queue in _queues) queue.Clear();
            }
        }
    }
}
