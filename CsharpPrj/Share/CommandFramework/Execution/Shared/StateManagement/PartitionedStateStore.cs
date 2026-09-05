
using System.Collections.Concurrent;
using Share.StateManagement;

namespace CommandFramework.Execution.Shared.StateManagement;

public class PartitionedStateStore : IPartitionedStateStore
{
    private readonly ConcurrentDictionary<string, Dictionary<string, object>> _partitions = new();
    private readonly ConcurrentDictionary<string, Dictionary<string, object>> _nodeResults = new();

    private static Dictionary<string, object> GetOrCreatePartition(
        ConcurrentDictionary<string, Dictionary<string, object>> source, string partitionId)
    {
        if (source.TryGetValue(partitionId, out var existing))
            return existing;
        return source.GetOrAdd(partitionId, _ => new Dictionary<string, object>());
    }

    public void Set(string partitionId, string key, object value)
    {
        var partition = GetOrCreatePartition(_partitions, partitionId);
        lock (partition)
        {
            partition[key] = value;
        }
    }

    public object? Get(string partitionId, string key)
    {
        if (_partitions.TryGetValue(partitionId, out var partition))
        {
            lock (partition)
            {
                if (partition.TryGetValue(key, out var value))
                    return value;
            }
        }
        return null;
    }

    public IDictionary<string, object> GetAll(string partitionId)
    {
        if (_partitions.TryGetValue(partitionId, out var partition))
        {
            lock (partition)
            {
                return new Dictionary<string, object>(partition);
            }
        }
        return new Dictionary<string, object>();
    }

    public void StoreNodeResult(string partitionId, string nodeKey, NodeExecutionResult result)
    {
        var results = GetOrCreatePartition(_nodeResults, partitionId);
        lock (results)
        {
            results[nodeKey] = result.Result;
        }

        if (result.OutputParameters != null && result.OutputParameters.Count > 0)
        {
            var partition = GetOrCreatePartition(_partitions, partitionId);
            lock (partition)
            {
                foreach (var kvp in result.OutputParameters)
                    partition[kvp.Key] = kvp.Value;
            }
        }
    }

    public object? GetNodeResult(string partitionId, string nodeKey)
    {
        if (_nodeResults.TryGetValue(partitionId, out var results))
        {
            lock (results)
            {
                if (results.TryGetValue(nodeKey, out var value))
                    return value;
            }
        }
        return null;
    }

    public IDictionary<string, object> GetAllNodeResults(string partitionId)
    {
        if (_nodeResults.TryGetValue(partitionId, out var results))
        {
            lock (results)
            {
                return new Dictionary<string, object>(results);
            }
        }
        return new Dictionary<string, object>();
    }

    public void Cleanup(string partitionId)
    {
        _partitions.TryRemove(partitionId, out _);
        _nodeResults.TryRemove(partitionId, out _);
    }
}
