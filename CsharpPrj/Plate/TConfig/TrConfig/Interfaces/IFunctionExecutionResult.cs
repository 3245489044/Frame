
namespace TConfig
{
    public interface IFunctionExecutionResult
    {
        bool IsSuccess { get; }

        object? Value { get; }

        string? ErrorMessage { get; }

        TimeSpan ExecutionTime { get; }
    }
}
