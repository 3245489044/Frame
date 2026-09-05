
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

        private IIsolatedStorage? _defaultStorage;
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
            catch
            {
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
            catch
            {
            }

            return GetBatchLogs(batchId);
        }

        public int GetLogCount(string batchId)
        {
            return TryQueryFromStorage(
                batchId,
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
            Func<IIsolatedStorage, T> query,
            Func<T, bool> isValid,
            Func<T> fallback)
        {
            return TryQueryFromStorage(batchId, GetIsolatedStorage(), query, isValid, fallback);
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
            catch
            {
            }

            return fallback();
        }

        public int FlushToIsolatedStorage(string batchId, IIsolatedStorage storage)
        {
            var logs = GetBatchLogs(batchId);
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
            if (_defaultStorage == null)
            {
                if (StorageFactory == null)
                    throw new InvalidOperationException(
                        "BatchRouter.StorageFactory 未设置。请在启动时注入：BatchRouter.StorageFactory = () => new SqliteIsolatedStorage();");

                lock (_lock)
                {
                    if (_defaultStorage == null)
                    {
                        _defaultStorage = StorageFactory();
                        _defaultStorage.Initialize();
                    }
                }
            }
            return _defaultStorage;
        }


        public void ResetStorage()
        {
            if (_defaultStorage != null)
            {
                try { _defaultStorage.Close(); } catch { }

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
