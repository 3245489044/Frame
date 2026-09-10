
namespace Share.StateManagement
{
    public class NodeExecutionRecord
    {
        public string NodeKey { get; set; } = string.Empty;

        public DateTime StartTime { get; set; }

        public DateTime? EndTime { get; set; }

        public bool Success { get; set; }

        public string? ErrorMessage { get; set; }

        public TimeSpan Duration => EndTime.HasValue
            ? EndTime.Value - StartTime
            : DateTime.UtcNow - StartTime;
    }
}
