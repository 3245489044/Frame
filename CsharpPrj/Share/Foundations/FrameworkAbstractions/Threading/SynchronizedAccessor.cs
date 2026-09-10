#nullable enable

namespace FrameworkAbstractions.Threading;

public sealed class SynchronizedAccessor<TState>
{
    private readonly TState _state;
    private readonly object _lock = new();

    public SynchronizedAccessor(TState state)
    {
        _state = state ?? throw new ArgumentNullException(nameof(state));
    }

    public TResult Read<TResult>(Func<TState, TResult> reader)
    {
        lock (_lock) { return reader(_state); }
    }

    public void Write(Action<TState> writer)
    {
        lock (_lock) { writer(_state); }
    }

    public TResult Write<TResult>(Func<TState, TResult> writer)
    {
        lock (_lock) { return writer(_state); }
    }
}