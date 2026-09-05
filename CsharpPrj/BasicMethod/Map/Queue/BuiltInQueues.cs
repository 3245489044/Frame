
#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;

namespace BasicMethod.Queue
{

    public class MemoryQueue<TMessage> : QueueBase<TMessage>, IQueueMonitor<TMessage>
    {
        private readonly Channel<TMessage> _channel;
        private readonly ChannelWriter<TMessage> _writer;
        private readonly ChannelReader<TMessage> _reader;

        public override int Count => _reader.Count;

        public long TotalEnqueued => _totalEnqueued;

        public long TotalDequeued => _totalDequeued;

        public long TotalProcessed => _totalProcessed;

        public long TotalFailed => _totalFailed;

        private long _totalEnqueued;
        private long _totalDequeued;
        private long _totalProcessed;
        private long _totalFailed;

        public MemoryQueue(string name, string? description = null, int? capacity = null)
            : base(name, description)
        {
            var options = new UnboundedChannelOptions
            {
                SingleWriter = false,
                SingleReader = false,
                AllowSynchronousContinuations = false
            };

            if (capacity.HasValue)
            {
                var bounded = new BoundedChannelOptions(capacity.Value)
                {
                    FullMode = BoundedChannelFullMode.Wait,
                    SingleWriter = false,
                    SingleReader = false
                };
                _channel = Channel.CreateBounded<TMessage>(bounded);
            }
            else
            {
                _channel = Channel.CreateUnbounded<TMessage>(options);
            }

            _writer = _channel.Writer;
            _reader = _channel.Reader;
        }

        public override async Task<QueueResult> EnqueueAsync(TMessage message, CancellationToken cancellationToken = default)
        {
            try
            {
                await _writer.WriteAsync(message, cancellationToken);
                Interlocked.Increment(ref _totalEnqueued);
                return QueueResult.Ok($"消息已入队到 '{Name}'");
            }
            catch (Exception ex)
            {
                return QueueResult.Fail($"入队失败: {ex.Message}", ex);
            }
        }

        public override async Task<(bool Success, TMessage? Message)> TryDequeueAsync(CancellationToken cancellationToken = default)
        {
            try
            {
                if (await _reader.WaitToReadAsync(cancellationToken))
                {
                    if (_reader.TryRead(out var message))
                    {
                        Interlocked.Increment(ref _totalDequeued);
                        return (true, message);
                    }
                }
                return (false, default);
            }
            catch (OperationCanceledException)
            {
                return (false, default);
            }
            catch (ChannelClosedException)
            {
                return (false, default);
            }
        }

        public override void Clear()
        {
            while (_reader.TryRead(out _)) { }
        }

        public QueueStats GetStats() => new(
            PendingCount: Count,
            ProcessingCount: 0,
            CompletedCount: (int)TotalProcessed,
            FailedCount: (int)TotalFailed,
            DeadLetterCount: 0,
            AverageWaitTime: TimeSpan.Zero,
            AverageProcessTime: TimeSpan.Zero
        );

        internal void RecordSuccess() => Interlocked.Increment(ref _totalProcessed);

        internal void RecordFailure() => Interlocked.Increment(ref _totalFailed);
    }


    public class MemoryPriorityQueue<TMessage> : QueueBase<TMessage>, IPriorityQueue<TMessage>
    {
        private readonly SortedSet<PriorityItem> _items;
        private readonly object _lock = new();
        private long _sequence;

        public override int Count { get { lock (_lock) return _items.Count; } }

        private record PriorityItem(int Priority, long Sequence, TMessage Message) : IComparable<PriorityItem>
        {
            public int CompareTo(PriorityItem? other)
            {
                if (other == null) return 1;
                var cmp = Priority.CompareTo(other.Priority);
                return cmp != 0 ? cmp : Sequence.CompareTo(other.Sequence);
            }
        }

        public MemoryPriorityQueue(string name, string? description = null)
            : base(name, description)
        {
            _items = new SortedSet<PriorityItem>();
        }

        public override Task<QueueResult> EnqueueAsync(TMessage message, CancellationToken cancellationToken = default)
            => EnqueueAsync(message, 0, cancellationToken);

        public Task<QueueResult> EnqueueAsync(TMessage message, int priority, CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                _items.Add(new PriorityItem(priority, _sequence++, message));
            }
            return Task.FromResult(QueueResult.Ok($"消息已入队到 '{Name}'（优先级 {priority}）"));
        }

        public override Task<(bool Success, TMessage? Message)> TryDequeueAsync(CancellationToken cancellationToken = default)
        {
            lock (_lock)
            {
                if (_items.Count > 0)
                {
                    var min = _items.Min;
                    if (min != null)
                    {
                        _items.Remove(min);
                        return Task.FromResult<(bool, TMessage?)>((true, min.Message));
                    }
                }
                return Task.FromResult<(bool, TMessage?)>((false, default));
            }
        }

        public override void Clear()
        {
            lock (_lock) _items.Clear();
        }
    }


    public class MemoryDelayedQueue<TMessage> : QueueBase<TMessage>, IDelayedQueue<TMessage>
    {
        private readonly MemoryQueue<TMessage> _readyQueue;
        private readonly List<DelayedItem> _delayedItems = new();
        private readonly object _lock = new();
        private Timer? _timer;

        public override int Count
        {
            get
            {
                lock (_lock)
                    return _readyQueue.Count + _delayedItems.Count;
            }
        }

        private record DelayedItem(TMessage Message, DateTime DueTime);

        public MemoryDelayedQueue(string name, string? description = null)
            : base(name, description)
        {
            _readyQueue = new MemoryQueue<TMessage>($"{name}.ready", "延迟队列的就绪子队列");
        }

        public override Task<QueueResult> EnqueueAsync(TMessage message, CancellationToken cancellationToken = default)
            => EnqueueAsync(message, TimeSpan.Zero, cancellationToken);

        public Task<QueueResult> EnqueueAsync(TMessage message, TimeSpan delay, CancellationToken cancellationToken = default)
        {
            if (delay <= TimeSpan.Zero)
            {
                return _readyQueue.EnqueueAsync(message, cancellationToken);
            }

            lock (_lock)
            {
                _delayedItems.Add(new DelayedItem(message, DateTime.UtcNow.Add(delay)));
                StartTimerIfNeeded();
            }

            return Task.FromResult(QueueResult.Ok($"消息已延迟入队到 '{Name}'（延迟 {delay.TotalSeconds:F1}s）"));
        }

        public override async Task<(bool Success, TMessage? Message)> TryDequeueAsync(CancellationToken cancellationToken = default)
        {
            MoveDueMessages();
            return await _readyQueue.TryDequeueAsync(cancellationToken);
        }

        public override void Clear()
        {
            lock (_lock)
            {
                _delayedItems.Clear();
                _timer?.Dispose();
                _timer = null;
            }
            _readyQueue.Clear();
        }

        private void MoveDueMessages()
        {
            List<TMessage> dueMessages;
            lock (_lock)
            {
                var now = DateTime.UtcNow;
                var dueItems = _delayedItems.Where(d => d.DueTime <= now).ToList();
                if (dueItems.Count == 0) return;

                dueMessages = dueItems.Select(d => d.Message).ToList();
                foreach (var item in dueItems)
                {
                    _delayedItems.Remove(item);
                }
            }

            foreach (var message in dueMessages)
            {
                try
                {
                    _ = _readyQueue.EnqueueAsync(message);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"延迟队列 '{Name}' 转移消息失败: {ex.Message}");
                }
            }
        }

        private void StartTimerIfNeeded()
        {
            if (_timer == null)
            {
                _timer = new Timer(_ => MoveDueMessages(), null, 100, 100);
            }
        }
    }


    public class MemoryBatchQueue<TMessage> : QueueBase<TMessage>, IBatchQueue<TMessage>
    {
        public int BatchSize { get; }

        public TimeSpan FlushInterval { get; }

        public override int Count { get { lock (_lock) return _buffer.Count; } }

        private readonly List<TMessage> _buffer = new();
        private readonly object _lock = new();
        private Timer? _flushTimer;
        private Func<IReadOnlyList<TMessage>, CancellationToken, Task<QueueResult>>? _batchHandler;
        private CancellationTokenSource? _batchCts;

        public MemoryBatchQueue(
            string name,
            int batchSize = 100,
            TimeSpan? flushInterval = null,
            string? description = null)
            : base(name, description)
        {
            BatchSize = batchSize > 0 ? batchSize : throw new ArgumentException("批量大小必须大于 0", nameof(batchSize));
            FlushInterval = flushInterval ?? TimeSpan.FromSeconds(5);
        }

        public override Task<QueueResult> EnqueueAsync(TMessage message, CancellationToken cancellationToken = default)
        {
            bool shouldFlush = false;

            lock (_lock)
            {
                _buffer.Add(message);
                if (_buffer.Count >= BatchSize)
                    shouldFlush = true;
            }

            if (shouldFlush)
                _ = FlushAsync(cancellationToken);

            return Task.FromResult(QueueResult.Ok($"消息已入队到批量队列 '{Name}'（当前 {Count}/{BatchSize}）"));
        }

        public override Task<(bool Success, TMessage? Message)> TryDequeueAsync(CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("批量队列不支持单条出队，请使用批量消费模式");
        }

        public Task StartConsumingAsync(
            Func<IReadOnlyList<TMessage>, CancellationToken, Task<QueueResult>> batchHandler,
            CancellationToken cancellationToken = default)
        {
            ArgumentNullException.ThrowIfNull(batchHandler);

            lock (_lock)
            {
                if (IsConsuming)
                    throw new InvalidOperationException($"批量队列 '{Name}' 已在消费中");

                _batchHandler = batchHandler;
                _batchCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                _flushTimer = new Timer(async _ => await FlushAsync(_batchCts.Token), null,
                    (int)FlushInterval.TotalMilliseconds, (int)FlushInterval.TotalMilliseconds);
            }

            return Task.CompletedTask;
        }

        public override Task StartConsumingAsync(
            Func<TMessage, CancellationToken, Task<QueueResult>> handler,
            CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("批量队列不支持单条消费模式，请使用批量消费 StartConsumingAsync(Func<IReadOnlyList<TMessage>, ...>)");
        }

        public override void Clear()
        {
            lock (_lock) _buffer.Clear();
        }

        private const int MaxFlushRetries = 3;

        private int _flushFailCount;

        private async Task FlushAsync(CancellationToken cancellationToken)
        {
            List<TMessage> batch;
            lock (_lock)
            {
                if (_buffer.Count == 0) return;
                batch = new List<TMessage>(_buffer);
                _buffer.Clear();
            }

            if (_batchHandler != null && batch.Count > 0)
            {
                try
                {
                    await _batchHandler(batch.AsReadOnly(), cancellationToken);
                    Interlocked.Exchange(ref _flushFailCount, 0);
                }
                catch (Exception ex)
                {
                    Interlocked.Increment(ref _flushFailCount);
                    int failCount = _flushFailCount;

                    if (failCount >= MaxFlushRetries)
                    {
                        Console.Error.WriteLine($"批量队列 '{Name}' 处理失败已达 {MaxFlushRetries} 次，丢弃 {batch.Count} 条消息: {ex.Message}");
                        Interlocked.Exchange(ref _flushFailCount, 0);
                    }
                    else
                    {
                        lock (_lock) _buffer.AddRange(batch);
                        Console.Error.WriteLine($"批量队列 '{Name}' 处理失败（第 {failCount} 次）: {ex.Message}");
                    }
                }
            }
        }
    }


    public class DeadLetterQueue<TMessage> : IDeadLetterQueue<TMessage>
    {
        public string Name { get; }

        public int Count
        {
            get { lock (_lock) return _deadLetters.Count; }
        }

        private readonly List<DeadLetterMessage<TMessage>> _deadLetters = new();
        private readonly object _lock = new();

        public DeadLetterQueue(string name)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
        }

        public Task DeadLetterAsync(TMessage message, string error, CancellationToken cancellationToken = default)
            => DeadLetterAsync(message, error, 1, cancellationToken);

        public Task DeadLetterAsync(TMessage message, string error, int deliveryAttempt, CancellationToken cancellationToken = default)
        {
            var deadMessage = new DeadLetterMessage<TMessage>(
                MessageId: Guid.NewGuid().ToString("N"),
                Payload: message,
                EnqueueTime: DateTime.UtcNow,
                DeadTime: DateTime.UtcNow,
                Error: error,
                DeliveryAttempt: deliveryAttempt
            );

            lock (_lock) _deadLetters.Add(deadMessage);
            return Task.CompletedTask;
        }

        public IReadOnlyList<DeadLetterMessage<TMessage>> GetDeadLetters()
        {
            lock (_lock) return _deadLetters.ToList().AsReadOnly();
        }

        public async Task<int> RetryAllAsync(IQueue<TMessage> targetQueue, CancellationToken cancellationToken = default)
        {
            List<DeadLetterMessage<TMessage>> toRetry;
            lock (_lock)
            {
                toRetry = new List<DeadLetterMessage<TMessage>>(_deadLetters);
                _deadLetters.Clear();
            }

            int successCount = 0;
            foreach (var dead in toRetry)
            {
                var result = await targetQueue.EnqueueAsync(dead.Payload, cancellationToken);
                if (result.IsSuccess) successCount++;
            }

            return successCount;
        }

        public void Clear()
        {
            lock (_lock) _deadLetters.Clear();
        }
    }
}
