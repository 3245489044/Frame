
#nullable enable
using System;
using System.Collections.Generic;

namespace BasicMethod.Queue
{

    public enum MessageStatus
    {
        Pending,

        Queued,

        Delivered,

        Processing,

        Completed,

        Failed,

        Dead
    }


    public record QueueResult(
        bool IsSuccess,
        string? Message = null,
        Exception? Exception = null
    )
    {
        public static QueueResult Ok(string? message = null)
            => new(true, message ?? "执行成功");

        public static QueueResult Fail(string? message = null, Exception? exception = null)
            => new(false, message ?? "执行失败", exception);

        public static QueueResult Skip(string? message = null)
            => new(true, message ?? "已跳过");
    }


    public class MessageEnvelope<TMessage>
    {
        public string MessageId { get; }

        public TMessage Payload { get; }

        public DateTime EnqueueTime { get; }

        public int DeliveryAttempt { get; set; }

        public MessageStatus Status { get; set; }

        public int Priority { get; }

        public TimeSpan? Delay { get; }

        public Dictionary<string, object>? Headers { get; set; }

        public MessageEnvelope(
            string messageId,
            TMessage payload,
            DateTime enqueueTime,
            int deliveryAttempt,
            MessageStatus status,
            int priority = 0,
            TimeSpan? delay = null,
            Dictionary<string, object>? headers = null)
        {
            MessageId = messageId;
            Payload = payload;
            EnqueueTime = enqueueTime;
            DeliveryAttempt = deliveryAttempt;
            Status = status;
            Priority = priority;
            Delay = delay;
            Headers = headers;
        }
    }


    public record DeadLetterMessage<TMessage>(
        string MessageId,
        TMessage Payload,
        DateTime EnqueueTime,
        DateTime DeadTime,
        string Error,
        int DeliveryAttempt
    );


    public record QueueStats(
        int PendingCount,
        int ProcessingCount,
        int CompletedCount,
        int FailedCount,
        int DeadLetterCount,
        TimeSpan AverageWaitTime,
        TimeSpan AverageProcessTime
    );


    public class QueueConsumeContext<TMessage>
    {
        public MessageEnvelope<TMessage> Envelope { get; }

        public CancellationToken CancellationToken { get; }

        public DateTime ConsumeStartTime { get; }

        public bool IsCancelled => CancellationToken.IsCancellationRequested;

        public QueueConsumeContext(MessageEnvelope<TMessage> envelope, CancellationToken cancellationToken)
        {
            Envelope = envelope ?? throw new ArgumentNullException(nameof(envelope));
            CancellationToken = cancellationToken;
            ConsumeStartTime = DateTime.UtcNow;
        }

        public THeader? GetHeader<THeader>(string key)
        {
            if (Envelope.Headers != null && Envelope.Headers.TryGetValue(key, out var value) && value is THeader typed)
                return typed;
            return default;
        }

        public void SetHeader(string key, object value)
        {
            Envelope.Headers ??= new Dictionary<string, object>();
            Envelope.Headers[key] = value;
        }
    }
}
