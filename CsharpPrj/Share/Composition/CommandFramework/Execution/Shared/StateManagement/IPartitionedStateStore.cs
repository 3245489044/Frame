
using Share.StateManagement;

namespace CommandFramework.Execution.Shared.StateManagement;

public interface IPartitionedStateStore
{
    void Set(string partitionId, string key, object value);

    object? Get(string partitionId, string key);

    IDictionary<string, object> GetAll(string partitionId);

    void StoreNodeResult(string partitionId, string nodeKey, NodeExecutionResult result);

    object? GetNodeResult(string partitionId, string nodeKey);

    IDictionary<string, object> GetAllNodeResults(string partitionId);

    void Cleanup(string partitionId);
}
