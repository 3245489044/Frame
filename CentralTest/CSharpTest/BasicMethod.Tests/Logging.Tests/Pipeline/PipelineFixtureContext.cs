
using Common.Diagnostics;

namespace Logging.Tests.Pipeline
{
    public class PipelineFixtureContext
    {
        public string? BatchId { get; set; }

        public string? BatchName { get; set; }

        public string? Source { get; set; }

        public string? ProjectRoot { get; set; }

        public List<LogEntry> GeneratedLogs { get; } = new();

        public LogPipelineContext? PipelineResult { get; set; }

        public bool StorageFailureMode { get; set; }
    }
}
