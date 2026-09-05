using ExecutionCenter.Abstractions;

namespace ExecutionCenter.Messages;

public class TaskResponse : ITaskResponse
{
    public Guid RequestMessageId { get; }
    public bool Success { get; }
    public string? Error { get; }
    public object? Result { get; init; }

    public TaskResponse(Guid requestMessageId, bool success, string? error = null, object? result = null)
    {
        RequestMessageId = requestMessageId;
        Success = success;
        Error = error;
        Result = result;
    }
}
