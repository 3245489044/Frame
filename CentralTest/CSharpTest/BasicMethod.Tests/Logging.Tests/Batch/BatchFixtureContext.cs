
using Common.Diagnostics;
using UniversalWorkflowFramework.BatchIntegration;

namespace Logging.Tests.Batch
{
    public class BatchFixtureContext
    {
        public string? CurrentBatchId { get; set; }

        public List<BatchMetadata> RegisteredBatches { get; } = new();

        public List<LogEntry> GeneratedLogs { get; } = new();

        public string? TempDbPath { get; set; }

        public IIsolatedStorage? Storage { get; set; }
    }
}
