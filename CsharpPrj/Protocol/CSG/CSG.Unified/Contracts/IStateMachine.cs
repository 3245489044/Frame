namespace CSG.Unified;

public interface IStateMachine
{
    string CurrentState { get; }

    bool CanTransitionTo(string state);

    bool TryTransitionTo(string state, object? trigger = null);

    event Action<string, string, string>? OnStateChanged;

    CanonicalForm ToCanonical();
}
