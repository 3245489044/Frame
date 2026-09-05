
namespace TConfig;

public class EventResult : IEventTriggerResult
{
    public bool IsSuccess { get; set; }

    public string? ErrorMessage { get; set; }

    public object? Value { get; set; }

    public string? ControlName { get; set; }

    public string? EventType { get; set; }

    public string? Message { get; set; }

    public bool IsWorkflowSwitchEvent { get; set; }

    public string? FunctionName { get; set; }

    public string? OutputTarget { get; set; }

    public TimeSpan ExecutionTime { get; set; }

    public ExecutionStatus Status { get; set; }

    public static EventResult SuccessResult(object? value, string controlName, string eventType,
        string? functionName = null, string? outputTarget = null, TimeSpan? executionTime = null)
        => new()
        {
            Status = ExecutionStatus.Completed,
            IsSuccess = true,
            Value = value,
            ControlName = controlName,
            EventType = eventType,
            FunctionName = functionName,
            OutputTarget = outputTarget,
            ExecutionTime = executionTime ?? TimeSpan.Zero
        };

    public static EventResult FailureResult(string errorMessage, string controlName, string eventType,
        string? functionName = null, TimeSpan? executionTime = null)
        => new()
        {
            Status = ExecutionStatus.Failed,
            IsSuccess = false,
            ErrorMessage = errorMessage,
            ControlName = controlName,
            EventType = eventType,
            FunctionName = functionName,
            ExecutionTime = executionTime ?? TimeSpan.Zero
        };
}
