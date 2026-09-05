#nullable enable

using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace DataManage.Abstractions
{
    public interface IDataStorage
    {
        Task InitializeAsync();
        Task SaveAsync<T>(string key, T data);
        Task<T?> LoadAsync<T>(string key);
        Task<bool> DeleteAsync(string key);
        Task<bool> ExistsAsync(string key);
        Task<IEnumerable<string>> GetKeysAsync();
        Task ClearAsync();

        Task StoreWorkflowDefaultParametersAsync(string workflowId, Dictionary<string, object> parameters);
        Task<Dictionary<string, object>> RetrieveWorkflowDefaultParametersAsync(string workflowId);
        Task DeleteWorkflowDefaultParametersAsync(string workflowId);
        Task<bool> ExistsWorkflowDefaultParametersAsync(string workflowId);
    }

    public interface IWorkflowStorage<T>
    {
        Task InitializeAsync();
        Task StoreAsync(string workflowId, T workflowData);
        Task<T?> RetrieveAsync(string workflowId);
        Task DeleteAsync(string workflowId);
        Task<bool> ExistsAsync(string workflowId);
        Task<List<string>> GetAllWorkflowIdsAsync();
        Task ClearAllWorkflowsAsync();
    }
}