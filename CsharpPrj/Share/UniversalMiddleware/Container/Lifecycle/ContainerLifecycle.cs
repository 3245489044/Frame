
#nullable enable
namespace UniversalMiddleware.Container.Lifecycle;

public enum ContainerLifecycleState
{
    Created,
    Initializing,
    Initialized,
    Loading,
    Loaded,
    Rendering,
    Rendered,
    Disposing,
    Disposed
}

public class ContainerLifecycle
{
    private ContainerLifecycleState _state = ContainerLifecycleState.Created;
    private readonly object _lock = new();

    public ContainerLifecycleState State
    {
        get { lock (_lock) return _state; }
    }

    public event EventHandler<ContainerLifecycleStateChangedEventArgs>? StateChanged;

    public bool TryTransitionTo(ContainerLifecycleState newState)
    {
        lock (_lock)
        {
            if (!IsValidTransition(_state, newState))
                return false;

            var oldState = _state;
            _state = newState;
            OnStateChanged(new ContainerLifecycleStateChangedEventArgs(oldState, newState));
            return true;
        }
    }

    public bool IsInState(ContainerLifecycleState state)
    {
        lock (_lock)
        {
            return _state == state;
        }
    }

    public bool IsInAnyState(params ContainerLifecycleState[] states)
    {
        lock (_lock)
        {
            return states.Contains(_state);
        }
    }

    public bool IsDisposed => IsInState(ContainerLifecycleState.Disposed);

    public bool IsInitialized => IsInState(ContainerLifecycleState.Initialized)
        || IsInState(ContainerLifecycleState.Loaded)
        || IsInState(ContainerLifecycleState.Rendered);

    private void OnStateChanged(ContainerLifecycleStateChangedEventArgs args)
    {
        StateChanged?.Invoke(this, args);
    }

    private static bool IsValidTransition(ContainerLifecycleState from, ContainerLifecycleState to)
    {
        return (from, to) switch
        {
            (ContainerLifecycleState.Created, ContainerLifecycleState.Initializing) => true,
            (ContainerLifecycleState.Initializing, ContainerLifecycleState.Initialized) => true,
            (ContainerLifecycleState.Initialized, ContainerLifecycleState.Loading) => true,
            (ContainerLifecycleState.Loading, ContainerLifecycleState.Loaded) => true,
            (ContainerLifecycleState.Loaded, ContainerLifecycleState.Rendering) => true,
            (ContainerLifecycleState.Rendering, ContainerLifecycleState.Rendered) => true,
            (ContainerLifecycleState.Rendered, ContainerLifecycleState.Loading) => true,
            (ContainerLifecycleState.Loaded, ContainerLifecycleState.Disposing) => true,
            (ContainerLifecycleState.Rendered, ContainerLifecycleState.Disposing) => true,
            (ContainerLifecycleState.Disposing, ContainerLifecycleState.Disposed) => true,
            _ => false
        };
    }
}

public class ContainerLifecycleStateChangedEventArgs : EventArgs
{
    public ContainerLifecycleState OldState { get; }

    public ContainerLifecycleState NewState { get; }

    public ContainerLifecycleStateChangedEventArgs(ContainerLifecycleState oldState, ContainerLifecycleState newState)
    {
        OldState = oldState;
        NewState = newState;
    }
}
