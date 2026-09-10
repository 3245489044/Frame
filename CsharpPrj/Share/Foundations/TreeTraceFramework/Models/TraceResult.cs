
namespace Share.TreeTraceFramework.Models
{
    public class TraceResult
    {
        public bool IsSuccess { get; protected set; }

        public bool IsFailure => !IsSuccess;

        public string? ErrorMessage { get; protected set; }

        public static TraceResult Success() =>
            new TraceResult { IsSuccess = true };

        public static TraceResult Failure(string errorMessage) =>
            new TraceResult { IsSuccess = false, ErrorMessage = errorMessage };
    }

    public class TraceResult<T> : TraceResult
    {
        public T? Value { get; protected set; }

        public static TraceResult<T> Success(T? value) =>
            new TraceResult<T> { IsSuccess = true, Value = value };

        public new static TraceResult<T> Failure(string errorMessage) =>
            new TraceResult<T> { IsSuccess = false, ErrorMessage = errorMessage };
    }
}
