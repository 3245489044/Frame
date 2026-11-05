namespace ExecutionCenter.Abstractions;

public interface ITaskResponse
{
    Guid RequestMessageId { get; }

    bool Success { get; }

    string? Error { get; }

    object? Result { get; }
}
