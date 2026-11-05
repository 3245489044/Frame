
using System.Collections.Generic;
using System.Linq;
using Common.DiscreteChange;
using UniversalWorkflowFramework.BatchIntegration;

namespace Common.Diagnostics
{
    /// <summary>LogEntry 批次差异比较器（按稳定字段组合判等，避免仅按消息文本折叠）。</summary>
    public class LogEntryMessageComparer : IEqualityComparer<LogEntry>
    {
        public bool Equals(LogEntry? x, LogEntry? y)
        {
            if (x == null && y == null) return true;
            if (x == null || y == null) return false;
            // 重复日志是常态：需按 Message + Level + Category + Module 组合判等（§4.16）。
            return x.Message == y.Message
                && x.Level == y.Level
                && x.Category == y.Category
                && x.Module == y.Module;
        }

        public int GetHashCode(LogEntry obj)
        {
            return HashCode.Combine(
                obj.Message?.GetHashCode() ?? 0,
                obj.Level?.GetHashCode() ?? 0,
                obj.Category,
                obj.Module?.GetHashCode() ?? 0);
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
            var listA = batchA?.ToList() ?? new List<LogEntry>();
            var listB = batchB?.ToList() ?? new List<LogEntry>();

            var tracker = new BatchChangeTracker<LogEntry>(new LogEntryMessageComparer());
            var report = tracker.CompareBatches(listA, listB,
                isModified: (old, @new) =>
                    old.Level != @new.Level ||
                    old.Message != @new.Message ||
                    old.Category != @new.Category);

            return new LogBatchDiffSummary
            {
                BatchACount = listA.Count,
                BatchBCount = listB.Count,
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
