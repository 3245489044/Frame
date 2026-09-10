
using System.Text.Json.Serialization;
using CommandFramework.Abstractions;
using CommandFramework.Execution;

namespace CommandFramework.Models;

public enum JsonErrorCode
{
    None = 0,

    UnknownError = 1000,

    InvalidArgument = 1001,

    Timeout = 1003,

    CommandEmpty = 1100,

    UnknownVerb = 1101,

    InvalidPath = 1102,

    AdapterNotFound = 1200,

    AdapterExecutionFailed = 1201,

    PathNotFound = 1300,

    PathExists = 1301,

    TypeMismatch = 1302,

    ValidationFailed = 1303,

    FileNotFound = 1400,

    FileWriteFailed = 1401,

    JsonParseFailed = 1402
}

public class JsonCommandResult : CommandExecutionResult<object>
{
    [JsonPropertyName("errorCode")]
    public JsonErrorCode JsonErrorCode { get; set; }

    [JsonPropertyName("durationMs")]
    public long DurationMs => (long)ExecutionTime.TotalMilliseconds;

    [JsonPropertyName("commandId")]
    public Guid CommandId { get; set; }

    public static JsonCommandResult Ok(object? value = null, Guid? commandId = null)
    {
        return new JsonCommandResult
        {
            IsSuccess = true,
            Value = value,
            JsonErrorCode = JsonErrorCode.None,
            CommandId = commandId ?? Guid.NewGuid()
        };
    }

    public static JsonCommandResult Fail(JsonErrorCode code, string error, Guid? commandId = null)
    {
        return new JsonCommandResult
        {
            IsSuccess = false,
            Error = error,
            JsonErrorCode = code,
            CommandId = commandId ?? Guid.NewGuid()
        };
    }

    public static JsonCommandResult FromExecutionResult(ICommandExecutionResult result)
    {
        if (result == null)
            return Fail(JsonErrorCode.UnknownError, "Execution result is null");

        return new JsonCommandResult
        {
            IsSuccess = result.IsSuccess,
            Value = result.Value,
            Error = result.Error,
            JsonErrorCode = result.IsSuccess ? JsonErrorCode.None : JsonErrorCode.UnknownError,
            ExecutionTime = TimeSpan.FromMilliseconds(result.ExecutionTimeMs),
            CommandId = Guid.NewGuid()
        };
    }

    public new JsonCommandResult WithExecutionTime(TimeSpan executionTime)
    {
        ExecutionTime = executionTime;
        return this;
    }
}
