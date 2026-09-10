
using System;
using System.Collections.Generic;
using System.Linq;
using Common.DiscreteChange;

namespace UniversalWorkflowFramework.BatchIntegration
{
    /// <summary>
    /// 批次差异对比（泛型）。复用 <see cref="DiscreteChangeCalculator{T}"/>，
    /// 与日志类型（Common.Diagnostics.LogEntry）解耦；日志维度由日志层扩展。
    /// </summary>
    public class BatchChangeTracker<T>
    {
        private readonly DiscreteChangeCalculator<T> _calculator;

        public BatchChangeTracker(IEqualityComparer<T>? comparer = null)
        {
            _calculator = new DiscreteChangeCalculator<T>(comparer ?? EqualityComparer<T>.Default);
        }

        public ChangeReport<T> CompareBatches(
            IEnumerable<T> batchA,
            IEnumerable<T> batchB,
            Func<T, T, bool>? isModified = null)
        {
            return _calculator.GenerateChangeReport(batchA, batchB, isModified);
        }

        public BatchDiffSummary CompareBatchesSummary(
            IEnumerable<T> batchA,
            IEnumerable<T> batchB,
            Func<T, T, bool>? isModified = null)
        {
            var report = CompareBatches(batchA, batchB, isModified);

            return new BatchDiffSummary
            {
                BatchACount = batchA?.Count() ?? 0,
                BatchBCount = batchB?.Count() ?? 0,
                Added = report.Summary.Added,
                Removed = report.Summary.Removed,
                Modified = report.Summary.Modified,
                Unchanged = report.Summary.Unchanged
            };
        }
    }

    /// <summary>批次差异摘要（通用计数；日志维度统计由日志层扩展）。</summary>
    public class BatchDiffSummary
    {
        public int BatchACount { get; set; }

        public int BatchBCount { get; set; }

        public int Added { get; set; }

        public int Removed { get; set; }

        public int Modified { get; set; }

        public int Unchanged { get; set; }

        public override string ToString()
        {
            var sb = new System.Text.StringBuilder();
            sb.AppendLine("===== 批次差异报告 =====");
            sb.AppendLine($"  源批次条目: {BatchACount} 条");
            sb.AppendLine($"  目标批次条目: {BatchBCount} 条");
            sb.AppendLine($"  新增: {Added}");
            sb.AppendLine($"  删除: {Removed}");
            sb.AppendLine($"  修改: {Modified}");
            sb.AppendLine($"  不变: {Unchanged}");
            return sb.ToString();
        }
    }
}

