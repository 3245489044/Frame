using System.Collections.Concurrent;
using Common.PlaceholderFramework.Core;

namespace Common.PlaceholderFramework.Storage
{
    public class MemoryMetadataStore : IMetadataStore
    {
        private readonly ConcurrentDictionary<string, List<PlaceholderMetadata>> _store = new();

        public void Save(PlaceholderMetadata metadata)
        {
            var batchId = metadata.BatchId ?? "default";
            _store.AddOrUpdate(
                batchId,
                _ => new List<PlaceholderMetadata> { metadata },
                (_, list) =>
                {
                    lock (list)
                    {
                        list.Add(metadata);
                    }
                    return list;
                });
        }

        public void SaveBatch(IEnumerable<PlaceholderMetadata> metadataList)
        {
            var grouped = metadataList.GroupBy(m => m.BatchId ?? "default");
            foreach (var group in grouped)
            {
                _store.AddOrUpdate(
                    group.Key,
                    _ => new List<PlaceholderMetadata>(group),
                    (_, list) =>
                    {
                        lock (list)
                        {
                            list.AddRange(group);
                        }
                        return list;
                    });
            }
        }

        public Task SaveBatchAsync(IEnumerable<PlaceholderMetadata> metadataList)
        {
            SaveBatch(metadataList);
            return Task.CompletedTask;
        }

        public IEnumerable<PlaceholderMetadata> FindByBatch(string batchId)
        {
            if (_store.TryGetValue(batchId, out var list))
            {
                lock (list)
                {
                    return list.ToList();
                }
            }
            return Enumerable.Empty<PlaceholderMetadata>();
        }

        private IEnumerable<PlaceholderMetadata> FindBy(string batchId, Func<PlaceholderMetadata, string?> propertySelector, string value)
        {
            return FindByBatch(batchId).Where(m =>
                string.Equals(propertySelector(m), value, StringComparison.OrdinalIgnoreCase));
        }

        public IEnumerable<PlaceholderMetadata> FindByModule(string batchId, string module)
            => FindBy(batchId, m => m.SourceModule, module);

        public IEnumerable<PlaceholderMetadata> FindByKind(string batchId, string kind)
            => FindBy(batchId, m => m.Kind, kind);

        public IEnumerable<PlaceholderMetadata> FindByTimeRange(string batchId, DateTime start, DateTime end)
            => FindByBatch(batchId).Where(m => m.Timestamp >= start && m.Timestamp <= end);

        public IEnumerable<PlaceholderMetadata> FindByKey(string batchId, string key)
            => FindBy(batchId, m => m.Key, key);

        public IEnumerable<PlaceholderMetadata> FindByNodeId(string batchId, string nodeId)
            => FindBy(batchId, m => m.NodeId, nodeId);

        public IEnumerable<string> GetAllBatchIds()
            => _store.Keys.ToList();

        public int GetCount(string batchId)
        {
            if (_store.TryGetValue(batchId, out var list))
            {
                lock (list)
                {
                    return list.Count;
                }
            }
            return 0;
        }

        public void ClearBatch(string batchId)
        {
            _store.TryRemove(batchId, out _);
        }

        public void ClearAll()
        {
            _store.Clear();
        }
    }
}
