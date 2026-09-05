#nullable enable

using System.Threading.Tasks;
using DataManage.Abstractions;

namespace DataManage
{
    public class UnifiedStorageManager
    {
        private readonly Abstractions.IDataStorage _dataStorage;
        private readonly Abstractions.IWorkflowStorage<string> _workflowStorage;
        private readonly Abstractions.IGraphStorage _graphStorage;

        public UnifiedStorageManager(
            Abstractions.IDataStorage dataStorage,
            Abstractions.IWorkflowStorage<string> workflowStorage,
            Abstractions.IGraphStorage graphStorage)
        {
            _dataStorage = dataStorage ?? throw new System.ArgumentNullException(nameof(dataStorage));
            _workflowStorage = workflowStorage ?? throw new System.ArgumentNullException(nameof(workflowStorage));
            _graphStorage = graphStorage ?? throw new System.ArgumentNullException(nameof(graphStorage));
        }

        public async Task InitializeAllAsync()
        {
            await _dataStorage.InitializeAsync();
            await _graphStorage.InitializeAsync();
        }

        public Abstractions.IDataStorage DataStorage => _dataStorage;
        public Abstractions.IWorkflowStorage<string> WorkflowStorage => _workflowStorage;
        public Abstractions.IGraphStorage GraphStorage => _graphStorage;

        public async Task<T?> LoadAsync<T>(string key) => await _dataStorage.LoadAsync<T>(key);
        public async Task SaveAsync<T>(string key, T data) => await _dataStorage.SaveAsync(key, data);
        public async Task<bool> DeleteAsync(string key) => await _dataStorage.DeleteAsync(key);
        public async Task<bool> ExistsAsync(string key) => await _dataStorage.ExistsAsync(key);
        public async Task<System.Collections.Generic.IEnumerable<string>> GetKeysAsync() => await _dataStorage.GetKeysAsync();

        public async Task<string?> LoadWorkflowAsync(string workflowId) => await _workflowStorage.RetrieveAsync(workflowId);
        public async Task SaveWorkflowAsync(string workflowId, string workflowJson) => await _workflowStorage.StoreAsync(workflowId, workflowJson);
        public async Task DeleteWorkflowAsync(string workflowId) => await _workflowStorage.DeleteAsync(workflowId);
        public async Task<bool> WorkflowExistsAsync(string workflowId) => await _workflowStorage.ExistsAsync(workflowId);

        public async Task<UniversalWorkflowFramework.Core.Models.Resource?> GetResourceAsync(UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId)
        {
            return await _graphStorage.GetResourceAsync(resourceId);
        }

        public async Task StoreResourceAsync(UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId,
            System.Collections.Generic.Dictionary<string, object> metadata, string? symbol = null)
        {
            await _graphStorage.StoreResourceAsync(resourceId, metadata, symbol);
        }

        public async Task DeleteResourceAsync(UniversalWorkflowFramework.Core.Models.ResourceIdentifier resourceId)
        {
            await _graphStorage.DeleteResourceAsync(resourceId);
        }
    }

    public static class UnifiedStorageManagerFactory
    {
        public static UnifiedStorageManager Create(string storageType = "DataMethodSQLite")
        {
            return Services.StorageFactory.CreateUnifiedStorageManager();
        }

        public static UnifiedStorageManager CreateDefault()
        {
            return Services.StorageFactory.CreateDefault();
        }

        public static UnifiedStorageManager CreateFrameManage()
        {
            return Services.StorageFactory.CreateFrameManage();
        }

        public static UnifiedStorageManager CreateWithEngine(Services.StorageEngine engine, string? storagePath = null)
        {
            return Services.StorageFactory.CreateUnifiedStorageManager(engine, storagePath);
        }
    }
}