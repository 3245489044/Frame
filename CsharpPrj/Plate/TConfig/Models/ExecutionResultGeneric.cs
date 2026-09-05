
namespace TConfig
{
    public class ExecutionResult<T> : ExecutionResult
    {
        public T? TypedValue => GetTypedValue();

        public static ExecutionResult<T> Success(T? value = default, TimeSpan? executionTime = null)
        {
            return new ExecutionResult<T>
            {
                IsSuccess = true,
                Value = value,
                ExecutionTime = executionTime ?? TimeSpan.Zero
            };
        }

        public static ExecutionResult<T> Ok(T? value = default, TimeSpan? executionTime = null)
            => Success(value, executionTime);

        public static ExecutionResult<T> Fail(string errorMessage, Exception? exception = null)
        {
            return new ExecutionResult<T>
            {
                IsSuccess = false,
                ErrorMessage = errorMessage,
                Exception = exception
            };
        }

        public new static ExecutionResult<T> Cancelled()
        {
            return new ExecutionResult<T>
            {
                IsSuccess = false,
                ErrorMessage = "CANCELLED"
            };
        }

        public static ExecutionResult<T> From(ExecutionResult result)
        {
            return new ExecutionResult<T>
            {
                IsSuccess = result.IsSuccess,
                Value = result.Value,
                ErrorMessage = result.ErrorMessage,
                ExecutionTime = result.ExecutionTime
            };
        }

        public T? GetTypedValue()
        {
            if (Value is T typed)
                return typed;
            try
            {
                return (T?)Convert.ChangeType(Value, typeof(T));
            }
            catch
            {
                return default;
            }
        }
    }
}
