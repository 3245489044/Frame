
namespace Share.StateManagement
{
    public interface IStateActor
    {
        string Name { get; }

        Task InitializeAsync();

        Task ShutdownAsync();

        TState? GetState<TState>(string key);

        void SetState<TState>(string key, TState value);
    }
}
