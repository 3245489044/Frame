
namespace TConfig;

public interface IFunctionExecutor
{
    Task<FunctionExecutionResult> ExecuteFunctionAsync(
        string functionName, Dictionary<string, object?> parameters);

    bool HasFunction(string functionName);


    Task<ExecutionResult> ExecuteAsync(string functionName, object?[]? parameters, IExecutionContext? context = null);

    Task<ExecutionResult> ExecuteAsync(string functionName, Dictionary<string, object> namedParameters, IExecutionContext? context = null);

    Task<ExecutionResult> ExecuteAsync(Delegate function, object? parameters, IExecutionContext? context = null);

    Task<ExecutionResult> ExecuteAsync(IFunctionUnit unit, object? input, IExecutionContext? context = null);

    Task<ExecutionResult> ExecuteAsync(ITrConfig config, object?[]? parameters, IExecutionContext? context = null);

    Task<ExecutionResult<TResult>> ExecuteAsync<TResult>(string functionName, object?[]? parameters, IExecutionContext? context = null);
}

public class FunctionExecutionResult
{
    public bool IsSuccess { get; init; }

    public object? Value { get; init; }

    public string? ErrorMessage { get; init; }

    public TimeSpan ExecutionTime { get; init; }

    public static FunctionExecutionResult Success(object? value = null, TimeSpan? executionTime = null)
        => new() { IsSuccess = true, Value = value, ExecutionTime = executionTime ?? TimeSpan.Zero };

    public static FunctionExecutionResult Failure(string errorMessage, TimeSpan? executionTime = null)
        => new() { IsSuccess = false, ErrorMessage = errorMessage, ExecutionTime = executionTime ?? TimeSpan.Zero };
}