
using System.Collections.Generic;
using System.Linq;
using UniversalWorkflowFramework.BatchIntegration;

namespace Common.Diagnostics
{
    /// <summary>
    /// 批次统计回填。订阅批次退出事件，按日志存储统计批次计数并回填到 BatchManager，
    /// 使 UR 批次上下文与日志统计单向解耦（UR 不引用日志类型）。
    /// </summary>
    public static class BatchStatisticsBackfill
    {
        private static readonly object _gate = new();
        private static bool _subscribed;

        public static void EnsureSubscribed()
        {
            lock (_gate)
            {
                if (_subscribed) return;
                _subscribed = true;
            }

            BatchManager.Instance.BatchExited += OnBatchExited;
        }

        private static void OnBatchExited(string batchId)
        {
            var logs = BatchRouter.Instance.GetBatchLogs(batchId) ?? new List<LogEntry>();
            var stats = new BatchStatistics(
                logs.Count,
                logs.Count(l => l.Level == "ERROR" || l.Level == "FATAL"),
                logs.Count(l => l.Level == "WARN"));
            BatchManager.Instance.UpdateStatistics(batchId, stats);
        }
    }
}
