
namespace Modules.Transformers
{
    public class Result<T>
    {
        public bool IsSuccess { get; }

        public T? Value { get; }

        public Exception? Exception { get; }

        public string? ErrorMessage => Exception?.Message;

        private Result(T value)
        {
            IsSuccess = true;
            Value = value;
            Exception = null;
        }

        private Result(Exception exception)
        {
            IsSuccess = false;
            Value = default;
            Exception = exception;
        }

        public static Result<T> Success(T value) => new(value);
        public static Result<T> Failure(Exception exception) => new(exception);

        public T GetValueOrThrow()
        {
            if (!IsSuccess) throw new InvalidOperationException(
                $"Result is not successful: {ErrorMessage}", Exception);
            return Value!;
        }

        public T? GetValueOrDefault(T? defaultValue = default)
            => IsSuccess ? Value : defaultValue;

        public override string ToString()
            => IsSuccess
                ? $"Success: {Value}"
                : $"Failure: {ErrorMessage}";
    }
}
