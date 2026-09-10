using System.Collections.Generic;
using System.Threading.Tasks;

namespace UniversalWorkflowFramework.Storage.Interfaces
{
    public interface IWorkflowStorage<T>
    {
        Task StoreAsync(string workflowId, T workflow);

        Task<T?> RetrieveAsync(string workflowId);

        Task DeleteAsync(string workflowId);

        Task<bool> ExistsAsync(string workflowId);

        Task<List<string>> GetAllWorkflowIdsAsync();
    }
}