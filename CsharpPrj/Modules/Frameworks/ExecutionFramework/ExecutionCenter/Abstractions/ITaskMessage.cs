namespace ExecutionCenter.Abstractions;

public interface ITaskMessage
{
    Guid MessageId { get; }

    string TaskType { get; }

    int Priority { get; }

    DateTime CreatedAt { get; }

    Dictionary<string, object>? SharedData { get; }
}
