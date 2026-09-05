
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UniversalWorkflowFramework.Core;

namespace UniversalWorkflowFramework.BatchIntegration
{
    public class BatchManager : IBatchContextProvider
    {

        private static readonly Lazy<BatchManager> _instance = new(() => new BatchManager());
        public static BatchManager Instance => _instance.Value;


        private readonly ContextManager<string> _contextManager = new();

        private readonly ConcurrentDictionary<string, BatchMetadata> _batches = new();

        /// <summary>批次进入事件（日志层订阅，替代旧批次上下文桥）。</summary>
        public event Action<string>? BatchEntered;

        /// <summary>批次退出事件（日志层订阅，替代旧批次上下文桥）。</summary>
        public event Action<string>? BatchExited;

        /// <summary>当前批次上下文唯一事实源（与日志层解耦）。</summary>
        public string? CurrentBatchId => _contextManager.Current;

        public IEnumerable<string> AllBatchIds => _batches.Keys;

        public IEnumerable<BatchMetadata> AllMetadata => _batches.Values;

        public int BatchCount => _batches.Count;

        public int RunningCount => _batches.Values.Count(b => b.Status == BatchStatus.Running);


        public bool RegisterBatch(string batchId, string name, string source,
            string? workflowId = null, string? description = null)
        {
            if (string.IsNullOrWhiteSpace(batchId))
                throw new ArgumentException("BatchId 不能为空", nameof(batchId));

            var metadata = new BatchMetadata
            {
                BatchId = batchId,
                Name = name,
                StartTime = DateTime.Now,
                Source = source,
                WorkflowId = workflowId,
                Status = BatchStatus.Running,
                Description = description
            };

            if (_batches.TryAdd(batchId, metadata))
            {
                _contextManager.Set(batchId);
                BatchEntered?.Invoke(batchId);
                return true;
            }
            return false;
        }

        internal void RegisterBatch(string batchId, BatchMetadata metadata)
        {
            if (string.IsNullOrWhiteSpace(batchId))
                throw new ArgumentException("BatchId 不能为空", nameof(batchId));

            metadata.BatchId = batchId;
            if (_batches.TryAdd(batchId, metadata))
            {
                _contextManager.Set(batchId);
                BatchEntered?.Invoke(batchId);
            }
        }


        public void SetCurrentBatch(string? batchId)
        {
            if (batchId == null || _batches.ContainsKey(batchId))
            {
                var previous = _contextManager.Current;
                if (batchId != null)
                {
                    _contextManager.Set(batchId);
                }
                else
                {
                    _contextManager.Clear();
                    if (previous != null)
                        BatchExited?.Invoke(previous);
                }
            }
            else
            {
                throw new KeyNotFoundException($"批次 {batchId} 未注册");
            }
        }

        public void CompleteBatch(string batchId)
        {
            if (_batches.TryGetValue(batchId, out var metadata))
            {
                metadata.Status = BatchStatus.Completed;
                metadata.EndTime = DateTime.Now;
            }
        }

        public void FailBatch(string batchId, string? errorDescription = null)
        {
            if (_batches.TryGetValue(batchId, out var metadata))
            {
                metadata.Status = BatchStatus.Failed;
                metadata.EndTime = DateTime.Now;
                metadata.Description = errorDescription;
            }
        }

        public void CancelBatch(string batchId)
        {
            if (_batches.TryGetValue(batchId, out var metadata))
            {
                metadata.Status = BatchStatus.Cancelled;
                metadata.EndTime = DateTime.Now;
            }
        }


        public BatchMetadata? GetBatch(string batchId)
        {
            return _batches.TryGetValue(batchId, out var metadata) ? metadata : null;
        }

        public IEnumerable<BatchMetadata> GetAllBatches()
        {
            return _batches.Values.OrderByDescending(b => b.StartTime).ToList();
        }


        public bool CleanupBatch(string batchId)
        {
            var removed = _batches.TryRemove(batchId, out _);
            if (removed)
            {
                if (_contextManager.Current == batchId)
                {
                    _contextManager.Clear();
                    BatchExited?.Invoke(batchId);
                }
            }
            return removed;
        }

        public void RemoveBatch(string batchId)
        {
            CleanupBatch(batchId);
        }

        public int CleanupOldBatches(int maxAgeDays = 7)
        {
            var cutoff = DateTime.Now.AddDays(-maxAgeDays);
            var toRemove = _batches.Values
                .Where(b => b.Status == BatchStatus.Completed && b.EndTime.HasValue && b.EndTime.Value < cutoff)
                .Select(b => b.BatchId)
                .ToList();

            foreach (var batchId in toRemove)
            {
                CleanupBatch(batchId);
            }

            return toRemove.Count;
        }


        // 注：原 ExecutePipelineAsync / CreateBatchPipeline 为基于 Frame.Pipeline（已废弃的自建管线）的重复实现，
        // 全库无调用方（死代码）。统一收敛到 Modules.Pipeline（Share.Pipeline），此处移除。



        /// <summary>更新批次统计（通用计数，不再依赖 Common.Diagnostics.LogEntry）。</summary>
        public void UpdateStatistics(string batchId, BatchStatistics stats)
        {
            if (_batches.TryGetValue(batchId, out var metadata))
            {
                metadata.LogCount = stats.LogCount;
                metadata.ErrorCount = stats.ErrorCount;
                metadata.WarnCount = stats.WarnCount;
            }
        }


        public void Reset()
        {
            _batches.Clear();
            _contextManager.Clear();
        }

        public string GetSummary()
        {
            var sb = new StringBuilder();
            sb.AppendLine($"批次总数: {BatchCount}");
            sb.AppendLine($"运行中: {RunningCount}");
            sb.AppendLine($"已完成: {_batches.Values.Count(b => b.Status == BatchStatus.Completed)}");
            sb.AppendLine($"失败: {_batches.Values.Count(b => b.Status == BatchStatus.Failed)}");
            sb.AppendLine($"已取消: {_batches.Values.Count(b => b.Status == BatchStatus.Cancelled)}");
            return sb.ToString();
        }
    }
}
