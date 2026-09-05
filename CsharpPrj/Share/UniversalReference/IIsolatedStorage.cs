
using Common.Diagnostics;

namespace UniversalWorkflowFramework
{
    public interface IIsolatedStorage
    {
        void Initialize();

        void Write(LogEntry entry);

        void WriteBatch(IEnumerable<LogEntry> entries);

        IEnumerable<LogEntry> Query(LogQueryFilter filter);

        int Count();

        void Clear();

        void ClearByBatchId(string batchId);

        List<LogEntry> QueryByBatchId(string batchId);

        int CountByBatchId(string batchId);

        void Close();
    }
}
