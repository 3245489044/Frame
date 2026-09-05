
using CommandLineResult = TConfig.CommandLineResult;

namespace BackEndDriven.Models;

public interface IExecutionResult
{
    bool Success { get; }

    int TotalCommands { get; }

    int SuccessCount { get; }

    int FailCount { get; }

    TimeSpan ExecutionTime { get; }

    string? ErrorMessage { get; }

    string? Mode { get; }

    string? BatchId { get; }
}

public class ExecutionResult : IExecutionResult
{
    public bool Success { get; set; }

    public int TotalCommands { get; set; }

    public int SuccessCount { get; set; }

    public int FailCount { get; set; }

    public List<CommandLineResult> CommandResults { get; set; } = new();

    public TimeSpan ExecutionTime { get; set; }

    public string? ErrorMessage { get; set; }

    public string? Mode { get; set; }

    public string? BatchId { get; set; }

    public string? FunctionResultValue { get; set; }

    public static ExecutionResult EmptySuccess(string mode = "CLI")
        => new()
        {
            Success = true,
            TotalCommands = 0,
            SuccessCount = 0,
            FailCount = 0,
            Mode = mode,
            ExecutionTime = TimeSpan.Zero
        };

    public static ExecutionResult Failure(string errorMessage, string mode = "CLI", int totalCommands = 0)
        => new()
        {
            Success = false,
            TotalCommands = totalCommands,
            SuccessCount = 0,
            FailCount = totalCommands > 0 ? totalCommands : 1,
            ErrorMessage = errorMessage,
            Mode = mode,
            ExecutionTime = TimeSpan.Zero
        };

    public static ExecutionResult FromContext(
        int totalCommands, int successCount, int failCount,
        List<CommandLineResult>? commandResults,
        TimeSpan executionTime, string? mode, string? batchId)
        => new()
        {
            Success = failCount == 0,
            TotalCommands = totalCommands,
            SuccessCount = successCount,
            FailCount = failCount,
            CommandResults = commandResults ?? new List<CommandLineResult>(),
            ExecutionTime = executionTime,
            Mode = mode ?? "CLI",
            BatchId = batchId
        };
}
