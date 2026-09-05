
#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace BasicMethod.Queue
{

    public interface IQueue<TMessage>
    {
        string Name { get; }

        int Count { get; }

        bool IsConsuming { get; }

        Task<QueueResult> EnqueueAsync(TMessage message, CancellationToken cancellationToken = default);

        Task<QueueResult> EnqueueBatchAsync(IEnumerable<TMessage> messages, CancellationToken cancellationToken = default);

        Task<(bool Success, TMessage? Message)> TryDequeueAsync(CancellationToken cancellationToken = default);

        Task StartConsumingAsync(
            Func<TMessage, CancellationToken, Task<QueueResult>> handler,
            CancellationToken cancellationToken = default);

        Task StopConsumingAsync();

        void Clear();
    }


    public interface IPriorityQueue<TMessage> : IQueue<TMessage>
    {
        Task<QueueResult> EnqueueAsync(TMessage message, int priority, CancellationToken cancellationToken = default);
    }


    public interface IDelayedQueue<TMessage> : IQueue<TMessage>
    {
        Task<QueueResult> EnqueueAsync(TMessage message, TimeSpan delay, CancellationToken cancellationToken = default);
    }


    public interface IBatchQueue<TMessage> : IQueue<TMessage>
    {
        int BatchSize { get; }

        TimeSpan FlushInterval { get; }

        Task StartConsumingAsync(
            Func<IReadOnlyList<TMessage>, CancellationToken, Task<QueueResult>> batchHandler,
            CancellationToken cancellationToken = default);
    }


    public interface IQueueHandler<TMessage>
    {
        string Name { get; }

        Task<QueueResult> HandleAsync(TMessage message, CancellationToken cancellationToken = default);
    }


    public interface IQueuePipeline<TMessage>
    {
        string Name { get; }

        IReadOnlyList<IQueueHandler<TMessage>> Handlers { get; }

        IQueuePipeline<TMessage> AddHandler(IQueueHandler<TMessage> handler);

        IQueuePipeline<TMessage> InsertBefore(string name, IQueueHandler<TMessage> handler);

        IQueuePipeline<TMessage> InsertAfter(string name, IQueueHandler<TMessage> handler);

        IQueuePipeline<TMessage> Remove(string name);

        Task<QueueResult> ExecuteAsync(TMessage message, CancellationToken cancellationToken = default);
    }


    public interface IQueueDispatcher
    {
        string Name { get; }

        IQueueDispatcher Register<TMessage>(string key, IQueue<TMessage> queue);

        IQueueDispatcher Register<TMessage>(string key, IQueueHandler<TMessage> handler);

        Task<QueueResult> DispatchAsync<TMessage>(string key, TMessage message, CancellationToken cancellationToken = default);

        Task<QueueResult> DispatchBatchAsync<TMessage>(string key, IEnumerable<TMessage> messages, CancellationToken cancellationToken = default);
    }


    public interface IDeadLetterQueue<TMessage>
    {
        string Name { get; }

        int Count { get; }

        Task DeadLetterAsync(TMessage message, string error, CancellationToken cancellationToken = default);

        IReadOnlyList<DeadLetterMessage<TMessage>> GetDeadLetters();

        Task<int> RetryAllAsync(IQueue<TMessage> targetQueue, CancellationToken cancellationToken = default);

        void Clear();
    }


    public interface IQueueMonitor<TMessage>
    {
        QueueStats GetStats();

        long TotalEnqueued { get; }

        long TotalDequeued { get; }

        long TotalProcessed { get; }

        long TotalFailed { get; }
    }
}
