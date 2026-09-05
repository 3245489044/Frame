
namespace TConfig;

public class ParameterExecutionResult
{
    public bool Success { get; init; }

    public object? Value { get; init; }

    public TimeSpan ExecutionTime { get; init; }

    public string? ErrorMessage { get; init; }

    public static ParameterExecutionResult Ok(object? value, TimeSpan executionTime)
        => new() { Success = true, Value = value, ExecutionTime = executionTime };

    public static ParameterExecutionResult Fail(string errorMessage)
        => new() { Success = false, ErrorMessage = errorMessage };
}
