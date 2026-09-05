
namespace Share.FrameworkAbstractions.Utilities
{
    public interface IExecutionResult
    {
        bool IsSuccess { get; }

        string? ErrorMessage { get; }

        string? ErrorCode { get; }

        bool IsFailure => !IsSuccess;
    }

    public class ExecutionResult<TContext> : IExecutionResult
    {
        public bool IsSuccess { get; set; }

        public string? ErrorMessage { get; set; }

        public string? ErrorCode { get; set; }

        public TContext? Context { get; set; }

        public TimeSpan? Duration { get; set; }

        public DateTime? StartedAt { get; set; }

        public DateTime? CompletedAt { get; set; }

        public static ExecutionResult<TContext> Success(TContext? context = default, TimeSpan? duration = null)
        {
            var now = DateTime.UtcNow;
            return new ExecutionResult<TContext>
            {
                IsSuccess = true,
                Context = context,
                StartedAt = duration.HasValue ? now - duration.Value : now,
                CompletedAt = now,
                Duration = duration
            };
        }

        public static ExecutionResult<TContext> Failure(string errorMessage, string? errorCode = null, TContext? context = default)
        {
            return new ExecutionResult<TContext>
            {
                IsSuccess = false,
                ErrorMessage = errorMessage,
                ErrorCode = errorCode,
                Context = context,
                CompletedAt = DateTime.UtcNow
            };
        }

        public static ExecutionResult<TContext> FromException(Exception ex, TContext? context = default)
        {
            return new ExecutionResult<TContext>
            {
                IsSuccess = false,
                ErrorMessage = ex.Message,
                ErrorCode = ex.GetType().Name,
                Context = context,
                CompletedAt = DateTime.UtcNow
            };
        }

        public static implicit operator bool(ExecutionResult<TContext> result) => result.IsSuccess;

        public override string ToString()
        {
            return IsSuccess
                ? $"Success [{Duration?.TotalMilliseconds:F0}ms]"
                : $"Failed: {ErrorCode ?? "Error"} - {ErrorMessage}";
        }
    }

    public class ExecutionResult : IExecutionResult
    {
        public bool IsSuccess { get; set; }

        public string? ErrorMessage { get; set; }

        public string? ErrorCode { get; set; }

        public TimeSpan? Duration { get; set; }

        public static ExecutionResult Success(TimeSpan? duration = null)
        {
            return new ExecutionResult { IsSuccess = true, Duration = duration };
        }

        public static ExecutionResult Failure(string errorMessage, string? errorCode = null)
        {
            return new ExecutionResult { IsSuccess = false, ErrorMessage = errorMessage, ErrorCode = errorCode };
        }

        public static ExecutionResult FromException(Exception ex)
        {
            return new ExecutionResult { IsSuccess = false, ErrorMessage = ex.Message, ErrorCode = ex.GetType().Name };
        }

        public static implicit operator bool(ExecutionResult result) => result.IsSuccess;

        public override string ToString()
        {
            return IsSuccess
                ? $"Success [{Duration?.TotalMilliseconds:F0}ms]"
                : $"Failed: {ErrorCode ?? "Error"} - {ErrorMessage}";
        }
    }
}
