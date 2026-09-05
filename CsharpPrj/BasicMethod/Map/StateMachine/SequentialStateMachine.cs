using System;
using System.Collections.Generic;
using System.Linq;

namespace Modules.StateMachine
{
    #region 顺序处理状态机

    public class SequentialStateMachine<TInput, TContext, TOutput> : IStateMachine<int>
        where TContext : new()
    {
        private readonly List<Transition> _transitions = new();
        private Transition? _otherwiseTransition;
        private bool _isCompleted;
        private readonly int _initialState;

        public int CurrentState { get; private set; }

        public TContext Context { get; private set; }

        public event EventHandler<StateChangedEventArgs<int>>? StateChanged
        {
            add => _emitter.Subscribe(value);
            remove => _emitter.Unsubscribe(value);
        }

        private readonly StateChangeEmitter<int> _emitter = new();

        public SequentialStateMachine(int initialState, TContext? context = default)
        {
            _initialState = initialState;
            CurrentState = initialState;
            Context = context ?? new TContext();
        }

        public SequentialStateMachine<TInput, TContext, TOutput> When(
            int fromState,
            Func<TInput, bool> condition,
            int toState,
            Action<TInput, TContext>? action = null)
        {
            if (condition == null)
                throw new ArgumentNullException(nameof(condition));

            _transitions.Add(new Transition(fromState, condition, toState, action));
            return this;
        }

        public SequentialStateMachine<TInput, TContext, TOutput> Otherwise(
            int toState,
            Action<TInput, TContext>? action = null)
        {
            _otherwiseTransition = new Transition(null, _ => true, toState, action);
            return this;
        }

        public bool Feed(TInput input)
        {
            if (_isCompleted)
                throw new InvalidOperationException("状态机已完成，无法继续 Feed。请先调用 Reset() 重置。");

            var transition = _transitions.FirstOrDefault(t =>
                t.FromState == CurrentState && t.Condition(input));

            if (transition == null && _otherwiseTransition != null)
            {
                transition = _otherwiseTransition;
            }

            if (transition == null)
                return false;

            var fromState = CurrentState;

            CurrentState = transition.ToState;

            _emitter.Raise(this, fromState, CurrentState);

            transition.Action?.Invoke(input, Context);

            return true;
        }

        public TOutput Complete(Func<TContext, TOutput> resultSelector)
        {
            if (resultSelector == null)
                throw new ArgumentNullException(nameof(resultSelector));

            _isCompleted = true;
            return resultSelector(Context);
        }

        public void Reset(int initialState, bool resetContext = true)
        {
            CurrentState = initialState;
            _isCompleted = false;
            if (resetContext)
            {
                Context = new TContext();
            }
        }

        public void Reset(bool resetContext = true)
        {
            Reset(_initialState, resetContext);
        }

        void IStateMachine<int>.Reset(int initialState)
        {
            Reset(initialState, resetContext: true);
        }

        public IReadOnlyList<(int FromState, int ToState)> GetPossibleTransitions()
        {
            return _transitions
                .Where(t => t.FromState == CurrentState)
                .Select(t => ((int)t.FromState!, t.ToState))
                .ToList();
        }

        #region 内部类型

        private class Transition
        {
            public int? FromState { get; }
            public Func<TInput, bool> Condition { get; }
            public int ToState { get; }
            public Action<TInput, TContext>? Action { get; }

            public Transition(int? fromState, Func<TInput, bool> condition, int toState, Action<TInput, TContext>? action)
            {
                FromState = fromState;
                Condition = condition;
                ToState = toState;
                Action = action;
            }
        }

        #endregion
    }

    #endregion
}
