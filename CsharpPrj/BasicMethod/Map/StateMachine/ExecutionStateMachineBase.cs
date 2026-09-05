using System;

namespace Modules.StateMachine
{
    #region 执行状态机基类

    public class ExecutionStateMachineBase<TState>
        where TState : struct, Enum
    {
        private readonly ConditionalStateMachine<TState> _stateMachine;
        private string? _errorMessage;
        private string? _errorCode;

        public ExecutionStateMachineBase(
            string id,
            TState initialStatus,
            TState runningState,
            TState pausedState)
        {
            if (string.IsNullOrWhiteSpace(id))
                throw new ArgumentException("ID cannot be empty", nameof(id));

            Id = id;
            _stateMachine = new ConditionalStateMachine<TState>(initialStatus);
            RunningState = runningState;
            PausedState = pausedState;
        }

        #region 子类配置

        protected TState RunningState { get; }

        protected TState PausedState { get; }

        protected virtual bool IsActiveState(TState state)
        {
            return EqualityComparer<TState>.Default.Equals(state, RunningState)
                || EqualityComparer<TState>.Default.Equals(state, PausedState);
        }

        protected virtual bool IsFinishedState(TState state)
        {
            return false;
        }

        #endregion

        #region 属性

        public string Id { get; }

        public TState Status => _stateMachine.CurrentState;

        public DateTime? StartTime => _stateMachine.StartTime;

        public DateTime? EndTime => _stateMachine.EndTime;

        public bool IsActive => IsActiveState(_stateMachine.CurrentState);

        public bool IsFinished => IsFinishedState(_stateMachine.CurrentState);

        public string? ErrorMessage => _errorMessage;

        public string? ErrorCode => _errorCode;

        public int ExecutionCount { get; private set; }

        public TimeSpan? Duration
        {
            get
            {
                if (_stateMachine.EndTime.HasValue)
                    return _stateMachine.EndTime.Value - _stateMachine.StartTime;

                if (EqualityComparer<TState>.Default.Equals(_stateMachine.CurrentState, RunningState))
                    return DateTime.UtcNow - _stateMachine.StartTime;

                return null;
            }
        }

        public event EventHandler<StateChangedEventArgs<TState>>? StateChanged
        {
            add => _stateMachine.StateChanged += value;
            remove => _stateMachine.StateChanged -= value;
        }

        #endregion

        #region 状态转换方法

        public bool MarkRunning()
        {
            if (_stateMachine.TryTransitionFrom(GetPendingState(), RunningState)
                || _stateMachine.TryTransitionFrom(PausedState, RunningState))
            {
                ExecutionCount++;
                return true;
            }
            return false;
        }

        public bool MarkCompleted()
        {
            return _stateMachine.TryTransitionFrom(RunningState, GetCompletedState());
        }

        public bool MarkFailed(string? errorMessage = null, string? errorCode = null)
        {
            if (_stateMachine.TryTransitionFrom(RunningState, GetFailedState()))
            {
                _errorMessage = errorMessage;
                _errorCode = errorCode;
                return true;
            }
            return false;
        }

        public bool MarkPaused()
        {
            return _stateMachine.TryTransitionFrom(RunningState, PausedState);
        }

        public bool MarkTimeout()
        {
            return _stateMachine.TryTransitionFrom(RunningState, GetTimeoutState());
        }

        public bool MarkCancelled()
        {
            return _stateMachine.TryTransitionFrom(GetPendingState(), GetCancelledState())
                || _stateMachine.TryTransitionFrom(RunningState, GetCancelledState());
        }

        public bool MarkSkipped()
        {
            return _stateMachine.TryTransitionFrom(GetPendingState(), GetSkippedState());
        }

        public void Reset(TState status)
        {
            _stateMachine.Reset(status);
            _errorMessage = null;
            _errorCode = null;
        }

        protected bool TryTransitionFrom(TState expectedState, TState newState)
        {
            return _stateMachine.TryTransitionFrom(expectedState, newState);
        }

        #endregion

        #region 子类可重写的状态值

        protected virtual TState GetPendingState() => default;

        protected virtual TState GetCompletedState() => default;

        protected virtual TState GetFailedState() => default;

        protected virtual TState GetTimeoutState() => default;

        protected virtual TState GetCancelledState() => default;

        protected virtual TState GetSkippedState() => default;

        #endregion

        #region 查询方法

        public bool IsInStatus(TState status) => _stateMachine.CurrentState.Equals(status);

        public bool CanTransitionTo(TState targetStatus)
        {
            return _stateMachine.TryTransitionTo(targetStatus);
        }

        public override string ToString()
        {
            return $"[{Id}] {Status}" +
                (ExecutionCount > 0 ? $" (x{ExecutionCount})" : "") +
                (StartTime.HasValue ? $" Start:{StartTime:HH:mm:ss.fff}" : "") +
                (EndTime.HasValue ? $" End:{EndTime:HH:mm:ss.fff}" : "") +
                (Duration.HasValue ? $" Duration:{Duration.Value.TotalSeconds:F1}s" : "") +
                (_errorMessage != null ? $" Error:{_errorMessage}" : "");
        }

        #endregion
    }

    #endregion
}
