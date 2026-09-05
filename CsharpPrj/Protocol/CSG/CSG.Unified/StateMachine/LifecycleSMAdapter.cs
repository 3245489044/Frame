using CSG.Unified;

namespace CSG.Unified.StateMachine;

public class LifecycleStateMachineAdapter<TOperation> : IStateMachine
    where TOperation : Enum
{
    private readonly object _stateMachine;
    private readonly Func<object, string> _getCurrentState;
    private readonly Func<object, string, bool> _canTransitionTo;
    private readonly Func<object, string, object?, bool> _tryTransitionTo;
    private readonly Func<object, CanonicalForm> _toCanonical;

    public LifecycleStateMachineAdapter(
        object stateMachine,
        Func<object, string> getCurrentState,
        Func<object, string, bool> canTransitionTo,
        Func<object, string, object?, bool> tryTransitionTo,
        Func<object, CanonicalForm>? toCanonical = null)
    {
        _stateMachine = stateMachine ?? throw new ArgumentNullException(nameof(stateMachine));
        _getCurrentState = getCurrentState ?? throw new ArgumentNullException(nameof(getCurrentState));
        _canTransitionTo = canTransitionTo ?? throw new ArgumentNullException(nameof(canTransitionTo));
        _tryTransitionTo = tryTransitionTo ?? throw new ArgumentNullException(nameof(tryTransitionTo));
        _toCanonical = toCanonical ?? DefaultToCanonical;
    }

    public string CurrentState => _getCurrentState(_stateMachine);

#pragma warning disable CS0067 // 事件由外部订阅者通过 IStateMachine 接口使用
    public event Action<string, string, string>? OnStateChanged;
#pragma warning restore CS0067

    public bool CanTransitionTo(string state) => _canTransitionTo(_stateMachine, state);

    public bool TryTransitionTo(string state, object? trigger = null)
        => _tryTransitionTo(_stateMachine, state, trigger);

    public CanonicalForm ToCanonical() => _toCanonical(_stateMachine);

    private static CanonicalForm DefaultToCanonical(object sm)
    {
        var form = new CanonicalForm();
        form.Metadata["source"] = "LifecycleStateMachine";
        form.Metadata["adapter"] = typeof(LifecycleStateMachineAdapter<TOperation>).Name;
        return form;
    }
}
