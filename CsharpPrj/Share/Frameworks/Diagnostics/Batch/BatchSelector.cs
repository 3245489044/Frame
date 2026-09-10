
using UniversalWorkflowFramework.BatchIntegration;

namespace Common.Diagnostics
{

    public static class BatchSelector
    {
        private static string? _selectedBatchId;

        public static string? GetCurrentBatch()
        {
            return _selectedBatchId ?? BatchManager.Instance.CurrentBatchId;
        }

        public static string GetCurrentBatchDisplayName()
        {
            var batchId = GetCurrentBatch();
            if (batchId == null)
                return "（无批次）";

            var metadata = BatchManager.Instance.GetBatch(batchId);

            if (metadata != null)
            {
                var status = metadata.Status == BatchStatus.Running ? "当前运行" : "历史批次";
                return $"{metadata.Name} ({status})";
            }

            return batchId;
        }

        public static void SwitchTo(string? batchId)
        {
            _selectedBatchId = batchId;
        }

        public static void ResetToCurrent()
        {
            _selectedBatchId = null;
        }

        public static bool IsViewingHistory()
        {
            return _selectedBatchId != null && _selectedBatchId != BatchManager.Instance.CurrentBatchId;
        }

        public static List<BatchDisplayItem> GetBatchDisplayList()
        {
            var items = new List<BatchDisplayItem>();
            var index = 1;

            foreach (var metadata in BatchManager.Instance.GetAllBatches())
            {
                var isCurrent = metadata.BatchId == BatchManager.Instance.CurrentBatchId;

                items.Add(new BatchDisplayItem
                {
                    Index = index++,
                    BatchId = metadata.BatchId,
                    DisplayName = isCurrent
                        ? $"{metadata.Name} (当前)"
                        : metadata.Name,
                    StartTime = metadata.StartTime,
                    Status = metadata.Status,
                    LogCount = metadata.LogCount,
                    ErrorCount = metadata.ErrorCount,
                    IsCurrent = isCurrent
                });
            }

            return items;
        }

        public static string? GetBatchIdByIndex(int index)
        {
            var items = GetBatchDisplayList();
            var item = items.FirstOrDefault(i => i.Index == index);
            return item?.BatchId;
        }

        public static string FormatBatchList()
        {
            var items = GetBatchDisplayList();
            if (items.Count == 0)
                return "  （暂无历史批次）";

            var lines = new List<string>();
            foreach (var item in items)
            {
                var timeStr = item.StartTime.ToString("MM-dd HH:mm");
                var statusStr = item.Status switch
                {
                    BatchStatus.Running => "运行中",
                    BatchStatus.Completed => "已完成",
                    BatchStatus.Failed => "失败",
                    BatchStatus.Cancelled => "已取消",
                    _ => ""
                };
                var currentMark = item.IsCurrent ? " ← 当前" : "";
                lines.Add($"  [{item.Index}] {item.DisplayName}  {timeStr}  {statusStr}  日志:{item.LogCount}  错误:{item.ErrorCount}{currentMark}");
            }

            return string.Join(Environment.NewLine, lines);
        }
    }

    public class BatchDisplayItem
    {
        public int Index { get; set; }

        public string BatchId { get; set; } = string.Empty;

        public string DisplayName { get; set; } = string.Empty;

        public DateTime StartTime { get; set; }

        public BatchStatus Status { get; set; }

        public int LogCount { get; set; }

        public int ErrorCount { get; set; }

        public bool IsCurrent { get; set; }
    }
}
