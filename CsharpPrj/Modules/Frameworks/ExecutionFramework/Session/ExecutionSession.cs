using Modules.StateMachine;

namespace ExecutionFramework.Session;

using Abstractions;
using TConfig;

public class ExecutionSession : IExecutionSession
{
    private readonly ConditionalStateMachine<ExecutionStatus> _stateMachine = new(ExecutionStatus.Pending);

    public string SessionId { get; }
    public string WorkflowId { get; }
    public IDictionary<string, object> Metadata { get; } = new Dictionary<string, object>();

    private string? _errorMessage;
    private Exception? _exception;

    public ExecutionSession(string workflowId, string? sessionId = null)
    {
        WorkflowId = workflowId ?? throw new ArgumentNullException(nameof(workflowId));
        SessionId = sessionId ?? Guid.NewGuid().ToString("N");
    }

    public ExecutionStatus Status => _stateMachine.CurrentState;

    public DateTime StartTime => _stateMachine.StartTime;

    public DateTime? EndTime => _stateMachine.EndTime;

    public virtual void MarkRunning()
    {
        if (!_stateMachine.TryTransitionFrom(ExecutionStatus.Pending, ExecutionStatus.Running))
        {
            _stateMachine.TryTransitionFrom(ExecutionStatus.Paused, ExecutionStatus.Running);
        }
    }

    public virtual void MarkCompleted()
    {
        if (!_stateMachine.TryTransitionFrom(ExecutionStatus.Running, ExecutionStatus.Completed))
        {
            throw new InvalidOperationException(
                $"Cannot mark session '{SessionId}' as Completed from current state '{Status}'");
        }
    }

    public virtual void MarkFailed(string? errorMessage = null, Exception? exception = null)
    {
        if (!_stateMachine.TryTransitionFrom(ExecutionStatus.Running, ExecutionStatus.Failed))
        {
            throw new InvalidOperationException(
                $"Cannot mark session '{SessionId}' as Failed from current state '{Status}'");
        }
        _errorMessage = errorMessage;
        _exception = exception;
    }

    public virtual void MarkCancelled()
    {
        if (!_stateMachine.TryTransitionTo(ExecutionStatus.Cancelled,
                s => s != ExecutionStatus.Completed
                  && s != ExecutionStatus.Failed
                  && s != ExecutionStatus.Cancelled))
        {
            throw new InvalidOperationException(
                $"Cannot mark session '{SessionId}' as Cancelled from current state '{Status}'");
        }
    }

    public virtual void MarkPaused()
    {
        if (!_stateMachine.TryTransitionFrom(ExecutionStatus.Running, ExecutionStatus.Paused))
        {
            throw new InvalidOperationException(
                $"Cannot mark session '{SessionId}' as Paused from current state '{Status}'");
        }
    }

    public virtual void MarkResumed()
    {
        MarkRunning();
    }

    public string? GetErrorMessage() => _errorMessage;

    public Exception? GetException() => _exception;

    public TimeSpan GetExecutionTime()
    {
        if (_stateMachine.EndTime.HasValue)
            return _stateMachine.EndTime.Value - _stateMachine.StartTime;
        return DateTime.UtcNow - _stateMachine.StartTime;
    }

    public event EventHandler<StateChangedEventArgs<ExecutionStatus>>? StateChanged
    {
        add => _stateMachine.StateChanged += value;
        remove => _stateMachine.StateChanged -= value;
    }

    public StateSnapshot<ExecutionStatus> GetStateSnapshot() => _stateMachine.GetSnapshot();


    protected bool TryTransitionFrom(ExecutionStatus from, ExecutionStatus to)
        => _stateMachine.TryTransitionFrom(from, to);

    protected bool TryTransitionTo(ExecutionStatus to, Func<ExecutionStatus, bool>? condition = null)
        => _stateMachine.TryTransitionTo(to, condition);

    protected void SetError(string? errorMessage, Exception? exception = null)
    {
        _errorMessage = errorMessage;
        _exception = exception;
    }
}
