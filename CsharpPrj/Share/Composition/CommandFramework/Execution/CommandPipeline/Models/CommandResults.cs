
using System.Diagnostics;
using System.Text.Json;
using System.Text.Json.Serialization;
using CommandFramework.Abstractions;
using TConfig;

namespace CommandFramework.Execution;


public class ExecutionResult
{
    public ExecutionStatus Status { get; set; }
    public object? Value { get; set; }
    public string? ErrorMessage { get; set; }
    public Exception? Exception { get; set; }
    public TimeSpan ExecutionTime { get; set; }

    public bool IsSuccess => Status == ExecutionStatus.Completed;

    public bool IsFailure => Status == ExecutionStatus.Failed;

    public bool IsCancelled => Status == ExecutionStatus.Cancelled;

    public static ExecutionResult Success(object? value = null, TimeSpan? executionTime = null) =>
        new()
        {
            Status = ExecutionStatus.Completed,
            Value = value,
            ExecutionTime = executionTime ?? TimeSpan.Zero
        };

    public static ExecutionResult Failure(string errorMessage, Exception? exception = null) =>
        new()
        {
            Status = ExecutionStatus.Failed,
            ErrorMessage = errorMessage,
            Exception = exception
        };

    public static ExecutionResult Cancelled() =>
        new() { Status = ExecutionStatus.Cancelled, ErrorMessage = "执行已被取消" };

    public static ExecutionResult Timeout() =>
        new() { Status = ExecutionStatus.Timeout };

    public static implicit operator TConfig.ExecutionResult(ExecutionResult result)
        => TConfig.ExecutionResult.Succeed(result.Value, result.ExecutionTime);


    public static implicit operator ExecutionResult(TConfig.ExecutionResult result)
        => new()
        {
            Status = result.IsSuccess ? ExecutionStatus.Completed : ExecutionStatus.Failed,
            Value = result.Value,
            ErrorMessage = result.ErrorMessage,
            Exception = result.Exception,
            ExecutionTime = result.ExecutionTime
        };

}

public class ExecutionResult<T> : ExecutionResult
{
    public T? TypedValue
    {
        get
        {
            if (Value is T typed)
                return typed;
            try { return (T?)Convert.ChangeType(Value, typeof(T)); }
            catch { return default; }
        }
    }

    public static ExecutionResult<T> Success(T? value = default, TimeSpan? executionTime = null)
        => new()
        {
            Status = ExecutionStatus.Completed,
            Value = value,
            ExecutionTime = executionTime ?? TimeSpan.Zero
        };

    public new static ExecutionResult<T> Failure(string errorMessage, Exception? exception = null)
        => new()
        {
            Status = ExecutionStatus.Failed,
            ErrorMessage = errorMessage,
            Exception = exception
        };

    public new static ExecutionResult<T> Cancelled()
        => new() { Status = ExecutionStatus.Cancelled, ErrorMessage = "执行已被取消" };

    public static ExecutionResult<T> From(ExecutionResult result)
        => new()
        {
            Status = result.Status,
            Value = result.Value,
            ErrorMessage = result.ErrorMessage,
            Exception = result.Exception,
            ExecutionTime = result.ExecutionTime
        };
}


public class CommandExecutionResult<T> : ICommandExecutionResult, IFunctionExecutionResult
{
    public bool IsSuccess { get; set; }

    public string Command { get; set; } = string.Empty;

    public T? Value { get; set; }

    public string? Error { get; set; }

    public string? ErrorCode { get; set; }

    public TimeSpan ExecutionTime { get; set; }

    public DateTime Timestamp { get; set; } = DateTime.Now;

    [JsonIgnore]
    long ICommandExecutionResult.ExecutionTimeMs => (long)ExecutionTime.TotalMilliseconds;

    [JsonIgnore]
    string? ICommandExecutionResult.Value => Value?.ToString();

    public string ToJson() => JsonSerializer.Serialize(this);


    [JsonIgnore]
    object? IFunctionExecutionResult.Value => Value;

    [JsonIgnore]
    string? IFunctionExecutionResult.ErrorMessage => Error;

    public static CommandExecutionResult<T> Success(T value, string command = "")
        => new() { IsSuccess = true, Command = command, Value = value };

    public static CommandExecutionResult<T> Failure(string error, string command = "", string? errorCode = null)
        => new() { IsSuccess = false, Command = command, Error = error, ErrorCode = errorCode };


    public CommandExecutionResult<T> WithExecutionTime(TimeSpan executionTime)
    {
        ExecutionTime = executionTime;
        return this;
    }

    public static implicit operator CommandExecutionResult<object?>(CommandExecutionResult<T> result)
        => new()
        {
            IsSuccess = result.IsSuccess,
            Command = result.Command,
            Value = result.Value,
            Error = result.Error,
            ErrorCode = result.ErrorCode,
            ExecutionTime = result.ExecutionTime,
            Timestamp = result.Timestamp
        };
}



public static class JsonCommandResult
{
    private static JsonSerializerOptions GetOptions(bool prettyPrint = false) => new()
    {
        WriteIndented = prettyPrint,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase
    };

    public static string Serialize(ICommandExecutionResult result, bool prettyPrint = false)
    {
        return JsonSerializer.Serialize(new
        {
            result.IsSuccess,
            result.Command,
            result.Value,
            result.Error,
            result.ErrorCode,
            executionTimeMs = result.ExecutionTimeMs
        }, GetOptions(prettyPrint));
    }

    public static string SerializeBatch(ICommandBatchResult batchResult, bool prettyPrint = false)
    {
        return JsonSerializer.Serialize(new
        {
            isAllSuccess = batchResult.IsAllSuccess,
            batchResult.TotalCount,
            batchResult.SuccessCount,
            batchResult.FailCount,
            batchResult.Results
        }, GetOptions(prettyPrint));
    }

    public static CommandExecutionResult<T>? Deserialize<T>(string json)
    {
        try
        {
            return JsonSerializer.Deserialize<CommandExecutionResult<T>>(json, GetOptions());
        }
        catch
        {
            return null;
        }
    }
}


