
namespace TConfig.ContextFrame.Abstractions;

public interface IStorageEngine : IDisposable
{
    string Name { get; }

    IStorageCell RootCell { get; }

    StorageEngineState State { get; }


    IStorageCell CreateSession(string sessionId);

    IStorageCell? GetSession(string sessionId);

    void DisposeSession(string sessionId);

    IEnumerable<string> GetActiveSessionIds();


    ILayeredSnapshot CreateSnapshot(string sessionId);

    void RestoreSnapshot(string sessionId, ILayeredSnapshot snapshot);


    event EventHandler<StorageEngineStateChangedEventArgs>? StateChanged;
}

public enum StorageEngineState
{
    Created,
    Running,
    Paused,
    Disposed
}

public class StorageEngineStateChangedEventArgs : EventArgs
{
    public StorageEngineState OldState { get; }

    public StorageEngineState NewState { get; }

    public string EngineName { get; }

    public StorageEngineStateChangedEventArgs(
        string engineName,
        StorageEngineState oldState,
        StorageEngineState newState)
    {
        EngineName = engineName;
        OldState = oldState;
        NewState = newState;
    }
}
