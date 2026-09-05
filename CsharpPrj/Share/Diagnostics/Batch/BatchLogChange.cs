
using System.Collections.Generic;
using System.Linq;
using Common.DiscreteChange;
using UniversalWorkflowFramework.BatchIntegration;

namespace Common.Diagnostics
{
    /// <summary>LogEntry 批次差异比较器（按消息文本去重）。</summary>
    public class LogEntryMessageComparer : IEqualityComparer<LogEntry>
    {
        public bool Equals(LogEntry? x, LogEntry? y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            return x.Message == y.Message;
        }

        public int GetHashCode(LogEntry obj)
        {
            return obj.Message?.GetHashCode() ?? 0;
        }
    }

    /// <summary>LogEntry 维度批次差异摘要（在通用 BatchDiffSummary 上扩展 Level/Category 统计）。</summary>
    public class LogBatchDiffSummary : BatchDiffSummary
    {
        public Dictionary<string, int> AddedByLevel { get; set; } = new();

        public Dictionary<string, int> RemovedByLevel { get; set; } = new();

        public Dictionary<string, int> AddedByCategory { get; set; } = new();

        public Dictionary<string, int> RemovedByCategory { get; set; } = new();
    }

    /// <summary>日志维度批次对比入口（通用批次对比 + LogEntry 维度统计）。</summary>
    public static class LogBatchChange
    {
        public static LogBatchDiffSummary CompareBatches(
            IEnumerable<LogEntry> batchA,
            IEnumerable<LogEntry> batchB)
        {
            var tracker = new BatchChangeTracker<LogEntry>(new LogEntryMessageComparer());
            var report = tracker.CompareBatches(batchA, batchB,
                isModified: (old, @new) =>
                    old.Level != @new.Level ||
                    old.Message != @new.Message ||
                    old.Category != @new.Category);

            return new LogBatchDiffSummary
            {
                BatchACount = batchA?.Count() ?? 0,
                BatchBCount = batchB?.Count() ?? 0,
                Added = report.Summary.Added,
                Removed = report.Summary.Removed,
                Modified = report.Summary.Modified,
                Unchanged = report.Summary.Unchanged,
                AddedByLevel = report.ChangeVector.Added
                    .GroupBy(l => l.Level)
                    .ToDictionary(g => g.Key, g => g.Count()),
                RemovedByLevel = report.ChangeVector.Removed
                    .GroupBy(l => l.Level)
                    .ToDictionary(g => g.Key, g => g.Count()),
                AddedByCategory = report.ChangeVector.Added
                    .GroupBy(l => l.Category.ToString())
                    .ToDictionary(g => g.Key, g => g.Count()),
                RemovedByCategory = report.ChangeVector.Removed
                    .GroupBy(l => l.Category.ToString())
                    .ToDictionary(g => g.Key, g => g.Count())
            };
        }
    }
}
