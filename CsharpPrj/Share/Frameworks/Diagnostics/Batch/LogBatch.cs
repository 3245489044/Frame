
using System;
using System.Collections.Generic;
using System.Linq;
using UniversalWorkflowFramework;
using UniversalWorkflowFramework.BatchIntegration;

namespace Common.Diagnostics
{

    public class BatchRouter
    {
        private static readonly Lazy<BatchRouter> _instance = new(() => new BatchRouter());
        public static BatchRouter Instance => _instance.Value;

        private volatile IIsolatedStorage? _defaultStorage;
        private readonly object _lock = new();

        private BatchRouter() { }


        public static Func<IIsolatedStorage>? StorageFactory { get; set; }

        public List<LogEntry> GetBatchLogs(string batchId)
        {
            if (string.IsNullOrWhiteSpace(batchId))
                return new List<LogEntry>();

            try
            {
                var storage = GetIsolatedStorage();
                var count = storage.CountByBatchId(batchId);
                if (count > 0)
                {
                    var results = storage.QueryByBatchId(batchId);
                    if (results != null && results.Count > 0)
                        return results.OrderBy(e => e.Timestamp).ToList();
                }
            }
            catch (Exception ex)
            {
                CrashDiag.Degrade($"BatchRouter 存储查询失败（批次 {batchId}），回退内存日志: {ex.Message}");
            }

            return LogRepository.GetAll()
                .Where(e => e.Extensions.TryGetValue("BatchId", out var val) && val?.ToString() == batchId)
                .OrderBy(e => e.Timestamp)
                .ToList();
        }

        public List<LogEntry> GetBatchLogs(string batchId, IIsolatedStorage storage)
        {
            if (string.IsNullOrWhiteSpace(batchId))
                return new List<LogEntry>();

            try
            {
                var results = storage.QueryByBatchId(batchId);
                if (results != null && results.Count > 0)
                    return results.OrderBy(e => e.Timestamp).ToList();
            }
            catch (Exception ex)
            {
                CrashDiag.Degrade($"BatchRouter 指定存储查询失败（批次 {batchId}）: {ex.Message}");
            }

            return GetBatchLogs(batchId);
        }

        public int GetLogCount(string batchId)
        {
            return TryQueryFromStorage(
                batchId,
                GetIsolatedStorage,
                storage => storage.CountByBatchId(batchId),
                count => count > 0,
                () => LogRepository.GetAll().Count(e =>
                    e.Extensions.TryGetValue("BatchId", out var val) && val?.ToString() == batchId));
        }

        public int GetLogCount(string batchId, IIsolatedStorage storage)
        {
            return TryQueryFromStorage(
                batchId,
                storage,
                s => s.CountByBatchId(batchId),
                count => count > 0,
                () => GetLogCount(batchId));
        }

        private T TryQueryFromStorage<T>(
            string batchId,
            Func<IIsolatedStorage> storageProvider,
            Func<IIsolatedStorage, T> query,
            Func<T, bool> isValid,
            Func<T> fallback)
        {
            if (string.IsNullOrWhiteSpace(batchId))
                return fallback();

            try
            {
                // 存储获取也必须在 try 内：StorageFactory 未注入时不得让异常直出（§4.2）。
                var storage = storageProvider();
                var result = query(storage);
                if (isValid(result))
                    return result;
            }
            catch (Exception ex)
            {
                CrashDiag.Degrade($"BatchRouter 默认存储计数失败（批次 {batchId}），回退内存日志: {ex.Message}");
            }

            return fallback();
        }

        private T TryQueryFromStorage<T>(
            string batchId,
            IIsolatedStorage storage,
            Func<IIsolatedStorage, T> query,
            Func<T, bool> isValid,
            Func<T> fallback)

        {
            if (string.IsNullOrWhiteSpace(batchId))
                return fallback();

            try
            {
                var result = query(storage);
                if (isValid(result))
                    return result;
            }
            catch (Exception ex)
            {
                CrashDiag.Degrade($"BatchRouter 指定存储计数失败（批次 {batchId}），回退默认源: {ex.Message}");
            }

            return fallback();
        }

        public int FlushToIsolatedStorage(string batchId, IIsolatedStorage storage)
        {
            // 读源=写目标：按传入 storage 取数，避免从默认库读取却写入传入库（§4.3）。
            var logs = GetBatchLogs(batchId, storage);
            if (logs.Count == 0) return 0;

            storage.ClearByBatchId(batchId);
            storage.WriteBatch(logs);
            return logs.Count;
        }

        public int FlushCurrentBatch(IIsolatedStorage storage)
        {
            var batchId = BatchManager.Instance.CurrentBatchId;
            if (string.IsNullOrEmpty(batchId)) return 0;
            return FlushToIsolatedStorage(batchId, storage);
        }

        public IIsolatedStorage GetIsolatedStorage()
        {
            var current = _defaultStorage;
            if (current != null)
                return current;

            lock (_lock)
            {
                if (_defaultStorage == null)
                {
                    if (StorageFactory == null)
                        throw new InvalidOperationException(
                            "BatchRouter.StorageFactory 未设置。请在启动时注入：BatchRouter.StorageFactory = () => new SqliteIsolatedStorage();");

                    var storage = StorageFactory();
                    // 先初始化再发布，避免并发线程看到未初始化实例（§4.13）。
                    storage.Initialize();
                    _defaultStorage = storage;
                }
                return _defaultStorage;
            }
        }


        public void ResetStorage()
        {
            if (_defaultStorage != null)
            {
                try { _defaultStorage.Close(); }
                catch (Exception ex) { CrashDiag.Degrade($"BatchRouter 释放默认存储失败: {ex.Message}"); }

                _defaultStorage = null;
            }
        }

        public int FlushToIsolatedStorage(string batchId)
        {
            return FlushToIsolatedStorage(batchId, GetIsolatedStorage());
        }

        public int FlushCurrentBatch()
        {
            return FlushCurrentBatch(GetIsolatedStorage());
        }
    }
}
