using System;
using System.Collections.Generic;

namespace Modules.StateMachine
{
    #region 状态记录

    public class StateRecord<TState>
    {
        public string InstanceId { get; }

        public TState Status { get; private set; }

        public string CurrentAction { get; private set; } = string.Empty;

        public DateTime StartTime { get; }

        public DateTime? EndTime { get; private set; }

        public TimeSpan Duration => (EndTime ?? DateTime.UtcNow) - StartTime;

        private readonly HashSet<string> _completedActions = new(StringComparer.Ordinal);

        private readonly Dictionary<string, string> _failedActions = new(StringComparer.Ordinal);

        public IReadOnlySet<string> CompletedActions => _completedActions;

        public IReadOnlyDictionary<string, string> FailedActions => _failedActions;

        public event Action<TState, TState>? OnStatusChanged;

        public StateRecord(string instanceId, TState initialState = default!)
        {
            InstanceId = instanceId ?? throw new ArgumentNullException(nameof(instanceId));
            Status = initialState;
            StartTime = DateTime.UtcNow;
        }

        public void SetStatus(TState newStatus)
        {
            var oldStatus = Status;
            Status = newStatus;
            OnStatusChanged?.Invoke(oldStatus, newStatus);
        }

        public void SetCurrentAction(string actionName)
        {
            CurrentAction = actionName ?? throw new ArgumentNullException(nameof(actionName));
        }

        public void AddCompletedAction(string actionName)
        {
            _completedActions.Add(actionName ?? throw new ArgumentNullException(nameof(actionName)));
        }

        public void AddFailedAction(string actionName, string error)
        {
            _failedActions[actionName ?? throw new ArgumentNullException(nameof(actionName))] = error;
        }

        public bool IsActionCompleted(string actionName) => _completedActions.Contains(actionName);

        public bool IsActionFailed(string actionName) => _failedActions.ContainsKey(actionName);

        public void Complete(TState completedState)
        {
            SetStatus(completedState);
            EndTime = DateTime.UtcNow;
        }

        public void Fault(TState faultedState, Exception ex)
        {
            SetStatus(faultedState);
            EndTime = DateTime.UtcNow;
        }

        public StateRecordSnapshot<TState> GetSnapshot()
        {
            return new StateRecordSnapshot<TState>
            {
                InstanceId = InstanceId,
                Status = Status,
                CurrentAction = CurrentAction,
                StartTime = StartTime,
                EndTime = EndTime,
                CompletedActions = new HashSet<string>(_completedActions),
                FailedActions = new Dictionary<string, string>(_failedActions)
            };
        }
    }

    public class StateRecordSnapshot<TState>
    {
        public string InstanceId { get; init; } = string.Empty;

        public TState Status { get; init; } = default!;

        public string CurrentAction { get; init; } = string.Empty;

        public DateTime StartTime { get; init; }

        public DateTime? EndTime { get; init; }

        public IReadOnlySet<string> CompletedActions { get; init; } = new HashSet<string>();

        public IReadOnlyDictionary<string, string> FailedActions { get; init; } = new Dictionary<string, string>();

        public TimeSpan Duration => (EndTime ?? DateTime.UtcNow) - StartTime;
    }

    #endregion
}
