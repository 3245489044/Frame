
namespace Share.StateManagement
{
    public class ErrorContext
    {
        public string ExecutionId { get; set; } = string.Empty;

        public string NodeKey { get; set; } = string.Empty;

        public string ErrorMessage { get; set; } = string.Empty;

        public DateTime Timestamp { get; set; }

        public int RetryCount { get; set; }
    }
}
