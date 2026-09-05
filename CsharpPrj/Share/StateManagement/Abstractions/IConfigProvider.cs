
namespace Share.StateManagement
{
    public interface IConfigProvider
    {
        Task<string> GetLatestVersionAsync(CancellationToken cancellationToken);

        Task<object?> LoadConfigAsync(string version, CancellationToken cancellationToken);
    }
}


namespace Share.StateManagement
{
    public interface IErrorHandler
    {
        Task<bool> HandleErrorAsync(ErrorContext errorContext, IStageContext context, CancellationToken cancellationToken);
    }
}

namespace Share.StateManagement
{
    public interface IExecutionParameterStore
    {
        void SetParameter(string executionId, string key, object? value);

        object? GetParameter(string executionId, string key);

        IDictionary<string, object?> GetParameters(string executionId);

        IDictionary<string, object> MergeParameters(string executionId, IDictionary<string, object>? nodeParameters);

        void StoreNodeResult(string executionId, string nodeKey, NodeExecutionResult result);

        object? GetNodeResult(string executionId, string nodeKey);

        void Cleanup(string executionId);

        bool HasParameters(string executionId);

        IEnumerable<string> GetActiveExecutionIds();
    }
}
namespace Share.StateManagement
{
    public interface IMetricsCollector
    {
        string Name { get; }

        Task CollectAsync(IStageContext context, CancellationToken cancellationToken);
    }
}
