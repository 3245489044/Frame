
using Common.Diagnostics;
using UniversalWorkflowFramework.BatchIntegration;

namespace Common.Diagnostics
{
    public class BatchEntryFactory : ILogEntryFactory
    {
        public static readonly BatchEntryFactory Instance = new();

        private static readonly DefaultEntryFactory _inner = DefaultEntryFactory.Instance;

        public LogEntry CreateEntry(
            string level,
            LogCategory category,
            string module,
            string message,
            Exception? exception,
            string? callerFile,
            int callerLine,
            string? callerMember)
        {
            var entry = _inner.CreateEntry(
                level, category, module, message, exception,
                callerFile, callerLine, callerMember);

            var batchId = BatchManager.Instance.CurrentBatchId;
            if (!string.IsNullOrEmpty(batchId))
            {
                entry.Extensions["BatchId"] = batchId;
            }

            return entry;
        }
    }
}
