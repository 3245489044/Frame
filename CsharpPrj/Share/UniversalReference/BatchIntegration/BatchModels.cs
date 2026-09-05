
namespace UniversalWorkflowFramework.BatchIntegration
{

    public enum BatchStatus
    {
        Running = 0,

        Completed = 1,

        Failed = 2,

        Cancelled = 3
    }


    public class BatchMetadata
    {
        public string BatchId { get; set; } = string.Empty;

        public string Name { get; set; } = string.Empty;

        public DateTime StartTime { get; set; } = DateTime.Now;

        public DateTime? EndTime { get; set; }

        public string Source { get; set; } = string.Empty;

        public string? WorkflowId { get; set; }

        public BatchStatus Status { get; set; } = BatchStatus.Running;

        public int LogCount { get; set; }

        public int ErrorCount { get; set; }

        public int WarnCount { get; set; }

        public string? Description { get; set; }
    }

    /// <summary>批次通用统计（与 Common.Diagnostics.LogEntry 解耦）。</summary>
    public record BatchStatistics(int LogCount, int ErrorCount, int WarnCount);
}
