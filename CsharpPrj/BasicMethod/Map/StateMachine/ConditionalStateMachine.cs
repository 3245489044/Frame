using System;
using System.Collections.Generic;

namespace Modules.StateMachine
{
    #region 条件转换状态机

    public class ConditionalStateMachine<TState> : IConditionalStateMachine<TState>
        where TState : struct, Enum
    {
        private TState _currentState;
        private readonly object _lock = new();

        public TState CurrentState
        {
            get
            {
                lock (_lock) return _currentState;
            }
        }

        public DateTime StartTime { get; private set; }

        public DateTime OriginalStartTime { get; private set; }

        public DateTime? EndTime { get; private set; }

        public bool IsActive
        {
            get
            {
                var state = CurrentState;
                return IsActiveState(state);
            }
        }

        public bool IsFinished
        {
            get
            {
                var state = CurrentState;
                return IsFinishedState(state);
            }
        }

        public event EventHandler<StateChangedEventArgs<TState>>? StateChanged
        {
            add => _emitter.Subscribe(value);
            remove => _emitter.Unsubscribe(value);
        }

        private readonly StateChangeEmitter<TState> _emitter = new();

        public ConditionalStateMachine(TState initialState = default)
        {
            _currentState = initialState;
            StartTime = DateTime.UtcNow;
            OriginalStartTime = StartTime;
        }

        public bool TryTransitionTo(TState newState, Func<TState, bool>? condition = null)
        {
            lock (_lock)
            {
                if (condition != null && !condition(_currentState))
                    return false;

                var oldState = _currentState;
                _currentState = newState;

                ApplyTransition(oldState, newState);
                return true;
            }
        }

        public bool TryTransitionFrom(TState expectedState, TState newState)
        {
            lock (_lock)
            {
                if (!EqualityComparer<TState>.Default.Equals(_currentState, expectedState))
                    return false;

                var oldState = _currentState;
                _currentState = newState;

                ApplyTransition(oldState, newState);
                return true;
            }
        }

        public void Reset(TState initialState)
        {
            lock (_lock)
            {
                var oldState = _currentState;
                _currentState = initialState;
                StartTime = DateTime.UtcNow;
                EndTime = null;

                _emitter.Raise(this, oldState, initialState);
            }
        }

        public StateSnapshot<TState> GetSnapshot()
        {
            lock (_lock)
            {
                return new StateSnapshot<TState>
                {
                    Status = _currentState,
                    StartTime = StartTime,
                    EndTime = EndTime,
                    IsActive = IsActiveState(_currentState),
                    IsFinished = IsFinishedState(_currentState)
                };
            }
        }

        protected virtual bool IsActiveState(TState state)
        {
            return false;
        }

        protected virtual bool IsFinishedState(TState state)
        {
            return false;
        }

        private void ApplyTransition(TState oldState, TState newState)
        {
            if (IsFinishedState(newState))
                EndTime = DateTime.UtcNow;

            _emitter.Raise(this, oldState, newState);
        }
    }

    public class StateSnapshot<TState>
        where TState : struct, Enum
    {
        public TState Status { get; set; }

        public DateTime StartTime { get; set; }

        public DateTime? EndTime { get; set; }

        public bool IsActive { get; set; }

        public bool IsFinished { get; set; }

        public TimeSpan Duration => EndTime.HasValue
            ? EndTime.Value - StartTime
            : DateTime.UtcNow - StartTime;
    }

    #endregion
}
