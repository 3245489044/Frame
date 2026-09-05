using System;
using System.Collections.Generic;
using System.Linq;

namespace Modules.StateMachine
{
    #region 生命周期状态机

    public class LifecycleStateMachine<TOperation> : IStateMachine<int>
        where TOperation : Enum
    {
        private readonly Dictionary<(int FromState, TOperation Operation), PermitTransition> _transitions = new();
        private readonly Dictionary<(int FromState, TOperation Operation), IgnoreTransition> _ignoreTransitions = new();

        public int CurrentState { get; private set; }

        public event EventHandler<StateChangedEventArgs<int>>? StateChanged
        {
            add => _emitter.Subscribe(value);
            remove => _emitter.Unsubscribe(value);
        }

        private readonly StateChangeEmitter<int> _emitter = new();

        public LifecycleStateMachine(int initialState)
        {
            CurrentState = initialState;
        }

        public LifecycleStateMachine<TOperation> Permit(
            int fromState,
            TOperation operation,
            int toState,
            Action? sideEffect = null)
        {
            var key = (fromState, operation);
            if (_transitions.ContainsKey(key))
                throw new StateMachineConfigurationException(
                    $"重复注册转换: 状态 '{fromState}' + 操作 '{operation}'");

            _transitions[key] = new PermitTransition(toState, sideEffect);
            return this;
        }

        public LifecycleStateMachine<TOperation> PermitReentry(
            int fromState,
            TOperation operation,
            Action? sideEffect = null)
        {
            var key = (fromState, operation);
            if (_ignoreTransitions.ContainsKey(key))
                throw new StateMachineConfigurationException(
                    $"重复注册忽略转换: 状态 '{fromState}' + 操作 '{operation}'");

            _ignoreTransitions[key] = new IgnoreTransition(sideEffect);
            return this;
        }

        public bool Fire(TOperation operation)
        {
            var key = (CurrentState, operation);

            if (_transitions.TryGetValue(key, out var permitTransition))
            {
                var fromState = CurrentState;
                CurrentState = permitTransition.ToState;

                _emitter.Raise(this, fromState, CurrentState, operation.ToString());

                permitTransition.SideEffect?.Invoke();
                return true;
            }

            if (_ignoreTransitions.TryGetValue(key, out var ignoreTransition))
            {
                ignoreTransition.SideEffect?.Invoke();
                return false;
            }

            throw new InvalidTransitionException(CurrentState, operation.ToString() ?? "unknown");
        }

        public bool CanFire(TOperation operation)
        {
            var key = (CurrentState, operation);
            return _transitions.ContainsKey(key) || _ignoreTransitions.ContainsKey(key);
        }

        public IEnumerable<TOperation> GetAllowedOperations()
        {
            return _transitions.Keys
                .Where(k => k.FromState == CurrentState)
                .Select(k => k.Operation)
                .Concat(_ignoreTransitions.Keys
                    .Where(k => k.FromState == CurrentState)
                    .Select(k => k.Operation))
                .Distinct();
        }

        public void Reset(int initialState)
        {
            CurrentState = initialState;
        }

        #region 内部类型

        private class PermitTransition
        {
            public int ToState { get; }
            public Action? SideEffect { get; }

            public PermitTransition(int toState, Action? sideEffect)
            {
                ToState = toState;
                SideEffect = sideEffect;
            }
        }

        private class IgnoreTransition
        {
            public Action? SideEffect { get; }

            public IgnoreTransition(Action? sideEffect)
            {
                SideEffect = sideEffect;
            }
        }

        #endregion
    }

    #endregion
}
