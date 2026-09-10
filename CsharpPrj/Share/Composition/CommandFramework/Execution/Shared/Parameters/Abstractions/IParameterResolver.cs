
#nullable enable
using System.Collections.Generic;
using Share.StateManagement;

namespace CommandFramework.Parameters.Abstractions
{
    public interface IParameterResolver
    {
        void SetParameter(string executionId, string key, object value);

        object? GetParameter(string executionId, string key);

        IDictionary<string, object> GetParameters(string executionId);

        IDictionary<string, object> MergeParameters(
            string executionId,
            IDictionary<string, object>? nodeParameters);

        void StoreNodeResult(
            string executionId,
            string nodeKey,
            NodeExecutionResult result);

        object? GetNodeResult(string executionId, string nodeKey);

        IDictionary<string, object> GetAllNodeResults(string executionId);

        void Cleanup(string executionId);
    }
}