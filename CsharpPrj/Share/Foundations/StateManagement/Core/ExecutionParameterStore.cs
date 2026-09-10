
using System.Collections.Concurrent;

namespace Share.StateManagement
{
    public class ExecutionParameterStore : IExecutionParameterStore
    {
        private readonly ConcurrentDictionary<string, Dictionary<string, object?>> _executionParameters = new(StringComparer.OrdinalIgnoreCase);
        private readonly ConcurrentDictionary<string, Dictionary<string, object?>> _nodeResults = new(StringComparer.OrdinalIgnoreCase);

        public void SetParameter(string executionId, string key, object? value)
        {
            var parameters = _executionParameters.GetOrAdd(executionId, _ => new Dictionary<string, object?>());
            lock (parameters)
            {
                parameters[key] = value;
            }
        }

        public object? GetParameter(string executionId, string key)
        {
            if (_executionParameters.TryGetValue(executionId, out var parameters))
            {
                lock (parameters)
                {
                    return parameters.TryGetValue(key, out var value) ? value : null;
                }
            }
            return null;
        }

        public IDictionary<string, object?> GetParameters(string executionId)
        {
            if (_executionParameters.TryGetValue(executionId, out var parameters))
            {
                lock (parameters)
                {
                    return new Dictionary<string, object?>(parameters);
                }
            }
            return new Dictionary<string, object?>();
        }

        public IDictionary<string, object> MergeParameters(string executionId, IDictionary<string, object>? nodeParameters)
        {
            var merged = new Dictionary<string, object>();

            if (_executionParameters.TryGetValue(executionId, out var parameters))
            {
                lock (parameters)
                {
                    foreach (var kvp in parameters)
                        merged[kvp.Key] = kvp.Value!;
                }
            }

            if (nodeParameters != null)
            {
                foreach (var kvp in nodeParameters)
                    merged[kvp.Key] = kvp.Value;
            }

            return merged;
        }

        public void StoreNodeResult(string executionId, string nodeKey, NodeExecutionResult result)
        {
            var results = _nodeResults.GetOrAdd(executionId, _ => new Dictionary<string, object?>());
            lock (results)
            {
                results[nodeKey] = result.Result;

                if (result.OutputParameters != null && result.OutputParameters.Count > 0)
                {
                    var parameters = _executionParameters.GetOrAdd(executionId, _ => new Dictionary<string, object?>());
                    lock (parameters)
                    {
                        foreach (var kvp in result.OutputParameters)
                            parameters[kvp.Key] = kvp.Value;
                    }
                }
            }
        }

        public object? GetNodeResult(string executionId, string nodeKey)
        {
            if (_nodeResults.TryGetValue(executionId, out var results))
            {
                lock (results)
                {
                    return results.TryGetValue(nodeKey, out var value) ? value : null;
                }
            }
            return null;
        }

        public void Cleanup(string executionId)
        {
            _executionParameters.TryRemove(executionId, out _);
            _nodeResults.TryRemove(executionId, out _);
        }

        public bool HasParameters(string executionId)
        {
            return _executionParameters.ContainsKey(executionId);
        }

        public IEnumerable<string> GetActiveExecutionIds()
        {
            return _executionParameters.Keys.ToArray();
        }
    }
}
