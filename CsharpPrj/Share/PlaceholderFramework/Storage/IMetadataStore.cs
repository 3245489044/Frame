using Common.PlaceholderFramework.Core;

namespace Common.PlaceholderFramework.Storage
{
    public interface IMetadataStore
    {
        void Save(PlaceholderMetadata metadata);

        void SaveBatch(IEnumerable<PlaceholderMetadata> metadataList);

        Task SaveBatchAsync(IEnumerable<PlaceholderMetadata> metadataList);

        IEnumerable<PlaceholderMetadata> FindByBatch(string batchId);

        IEnumerable<PlaceholderMetadata> FindByModule(string batchId, string module);

        IEnumerable<PlaceholderMetadata> FindByKind(string batchId, string kind);

        IEnumerable<PlaceholderMetadata> FindByTimeRange(string batchId, DateTime start, DateTime end);

        IEnumerable<PlaceholderMetadata> FindByKey(string batchId, string key);

        IEnumerable<PlaceholderMetadata> FindByNodeId(string batchId, string nodeId);

        IEnumerable<string> GetAllBatchIds();

        int GetCount(string batchId);

        void ClearBatch(string batchId);

        void ClearAll();
    }
}
