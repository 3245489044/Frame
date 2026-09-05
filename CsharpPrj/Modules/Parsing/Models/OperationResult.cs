namespace Modules.Parsing.Models;

public class OperationResult<T>
{
    public bool Success { get; }
    public string Message { get; }
    public T? Data { get; }
    public Exception? Exception { get; }
    public ErrorCode ErrorCode { get; }
    public Guid? CommandId { get; }

    private OperationResult(bool success, string message, T? data = default, Exception? exception = null,
                           ErrorCode errorCode = ErrorCode.None, Guid? commandId = null)
    {
        Success = success;
        Message = message;
        Data = data;
        Exception = exception;
        ErrorCode = errorCode;
        CommandId = commandId;
    }

    public static OperationResult<T> SuccessResult(T? data, string message, Guid? commandId = null)
        => new(true, message, data, null, ErrorCode.None, commandId);

    public static OperationResult<T> FailureResult(string message, ErrorCode errorCode = ErrorCode.UnknownError,
                                                    Exception? exception = null, Guid? commandId = null)
        => new(false, message, default, exception, errorCode, commandId);

    public OperationResult<object> ToObjectResult()
    {
        if (Success)
            return OperationResult<object>.SuccessResult(Data, Message, CommandId ?? Guid.Empty);
        return OperationResult<object>.FailureResult(Message, ErrorCode, Exception, CommandId ?? Guid.Empty);
    }

    public bool IsFatalError() => ErrorCode.IsFatal();

    public override string ToString()
    {
        return $"Success: {Success}, ErrorCode: {ErrorCode}, Message: {Message}, CommandId: {CommandId}";
    }
}
