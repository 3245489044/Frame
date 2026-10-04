
#nullable enable
using System.Collections.Generic;
using Share.StateManagement;
using CommandFramework.Parameters.Abstractions;
using CommandFramework.Execution.Shared.StateManagement;

namespace CommandFramework.Parameters
{
    public class ParameterResolver : IParameterResolver
    {
        private readonly IPartitionedStateStore _stateStore;

        public ParameterResolver()
            : this(new PartitionedStateStore())
        {
        }

        public ParameterResolver(IPartitionedStateStore stateStore)
        {
            _stateStore = stateStore ?? throw new ArgumentNullException(nameof(stateStore));
        }

        public void SetParameter(string executionId, string key, object value)
            => _stateStore.Set(executionId, key, value);

        public object? GetParameter(string executionId, string key)
            => _stateStore.Get(executionId, key);

        public IDictionary<string, object> GetParameters(string executionId)
            => _stateStore.GetAll(executionId);

        public IDictionary<string, object> MergeParameters(
            string executionId,
            IDictionary<string, object>? nodeParameters)
        {
            var merged = new Dictionary<string, object>(_stateStore.GetAll(executionId));

            if (nodeParameters != null)
            {
                foreach (var kvp in nodeParameters)
                    merged[kvp.Key] = kvp.Value;
            }

            return merged;
        }

        public void StoreNodeResult(
            string executionId,
            string nodeKey,
            NodeExecutionResult result)
            => _stateStore.StoreNodeResult(executionId, nodeKey, result);

        public object? GetNodeResult(string executionId, string nodeKey)
            => _stateStore.GetNodeResult(executionId, nodeKey);

        public IDictionary<string, object> GetAllNodeResults(string executionId)
            => _stateStore.GetAllNodeResults(executionId);

        public void Cleanup(string executionId)
            => _stateStore.Cleanup(executionId);
    }
}
