namespace CentralConsole.AsyncSyncBridge
{
    public interface IDispatcher
    {
        bool InvokeRequired { get; }

        void Invoke(Action action);

        T Invoke<T>(Func<T> action);

        Task InvokeAsync(Func<Task> action);

        Task<T> InvokeAsync<T>(Func<Task<T>> action);
    }
}
