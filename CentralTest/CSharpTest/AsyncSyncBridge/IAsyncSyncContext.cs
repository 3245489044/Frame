namespace CentralConsole.AsyncSyncBridge
{
    public interface IAsyncSyncContext : IDisposable
    {
        bool IsInitialized { get; }

        IDispatcher Dispatcher { get; }

        void Initialize();

        void Cleanup();

        Task RunAsync(Func<Task> action);

        Task<T> RunAsync<T>(Func<Task<T>> action);

        void RunSync(Action action);

        T RunSync<T>(Func<T> action);

        bool PumpUntil(Func<bool> condition, int timeoutMs = 30000);
    }
}
