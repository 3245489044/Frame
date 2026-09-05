
namespace TConfig
{
    public interface IExecutionContext
    {
        string ExecutionId { get; }

        string WorkflowId { get; }

        string NodeKey { get; }

        IReadOnlyDictionary<string, object> Parameters { get; }

        IDictionary<string, object> Items { get; }

        CancellationToken CancellationToken { get; set; }

        IServiceProvider? ServiceProvider { get; set; }

        T GetParameter<T>(string key);

        T GetParameter<T>(int index);

        bool TryGetParameter<T>(string key, out T value);

        T GetData<T>(string key);

        bool TryGetData<T>(string key, out T value);

        void SetData<T>(string key, T value);

        IExecutionContext CreateSubContext(string? nodeKey = null);
    }
}
