
#nullable enable

using System;

namespace GraphFramework.Models
{
    public class Result
    {
        public bool IsSuccess { get; protected set; }

        public Exception? Error { get; protected set; }

        public string? ErrorMessage { get; protected set; }

        public TimeSpan? Duration { get; set; }

        protected Result(bool isSuccess, Exception? error, string? errorMessage)
        {
            IsSuccess = isSuccess;
            Error = error;
            ErrorMessage = errorMessage;
        }

        public static Result Success() => new(true, null, null);

        public static Result Failure(string errorMessage) => new(false, null, errorMessage);

        public static Result Failure(Exception ex) => new(false, ex, ex.Message);

        public void ThrowIfFailed()
        {
            if (!IsSuccess)
                throw Error ?? new InvalidOperationException(ErrorMessage ?? "未知错误");
        }

        public override string ToString() =>
            IsSuccess
                ? $"Result [Success, Duration={Duration?.TotalMilliseconds:F0}ms]"
                : $"Result [Failed: {ErrorMessage}]";
    }

    public class Result<T> : Result
    {
        public T? Value { get; }

        protected Result(bool isSuccess, T? value, Exception? error, string? errorMessage)
            : base(isSuccess, error, errorMessage)
        {
            Value = value;
        }

        public static Result<T> Success(T value) => new(true, value, null, null);

        public static new Result<T> Failure(string errorMessage) => new(false, default, null, errorMessage);

        public static new Result<T> Failure(Exception ex) => new(false, default, ex, ex.Message);

        public T GetValueOrThrow()
        {
            if (!IsSuccess)
                throw Error ?? new InvalidOperationException(ErrorMessage ?? "未知错误");
            return Value!;
        }

        public T? GetValueOrDefault(T? defaultValue = default) => IsSuccess ? Value : defaultValue;

        public override string ToString() =>
            IsSuccess
                ? $"Result<{typeof(T).Name}> [Success, Value={Value}, Duration={Duration?.TotalMilliseconds:F0}ms]"
                : $"Result<{typeof(T).Name}> [Failed: {ErrorMessage}]";
    }
}
