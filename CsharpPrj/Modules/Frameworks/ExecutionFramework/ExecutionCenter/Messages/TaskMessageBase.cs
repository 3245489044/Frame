using ExecutionCenter.Abstractions;

namespace ExecutionCenter.Messages;

public abstract class TaskMessageBase : ITaskMessage
{
    public Guid MessageId { get; } = Guid.NewGuid();
    public abstract string TaskType { get; }
    public int Priority { get; init; } = 2;
    public DateTime CreatedAt { get; } = DateTime.UtcNow;

    public Dictionary<string, object>? SharedData { get; init; }
}
