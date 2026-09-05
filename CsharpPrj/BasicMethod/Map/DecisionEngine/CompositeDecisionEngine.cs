using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace BasicMethod.DecisionEngine
{
    #region 组合决策引擎

    public class CompositeDecisionEngine<TState, TContext> : IDecisionEngine<TState, TContext>
    {
        private readonly List<EngineEntry> _engines = new();
        private TState _currentState;
        private readonly TState _initialState;

        public string Name { get; }

        public TState CurrentState => _currentState;

        public int EngineCount => _engines.Count;

        public CompositeDecisionEngine(TState initialState, string name = "Composite")
        {
            _initialState = initialState;
            _currentState = initialState;
            Name = name;
        }

        public CompositeDecisionEngine<TState, TContext> AddEngine(
            IDecisionEngine<TState, TContext> engine,
            bool fallthrough = false)
        {
            if (engine == null)
                throw new ArgumentNullException(nameof(engine));

            _engines.Add(new EngineEntry(engine, fallthrough));
            return this;
        }

        public async Task<DecisionResult<TState>> DecideAsync(TContext context, CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            DecisionResult<TState>? lastValidResult = null;
            var attemptedEngines = new List<string>();

            foreach (var entry in _engines)
            {
                cancellationToken.ThrowIfCancellationRequested();

                attemptedEngines.Add(entry.Engine.Name);

                entry.Engine.SetCurrentState(_currentState);

                var result = await entry.Engine.DecideAsync(context, cancellationToken);

                _currentState = result.NextState;

                if (!result.IsCompleted)
                {
                    lastValidResult = result;

                    if (!entry.Fallthrough)
                    {
                        return EnhanceReasoning(lastValidResult, attemptedEngines);
                    }
                }
            }

            if (lastValidResult != null)
            {
                return EnhanceReasoning(lastValidResult, attemptedEngines);
            }

            return new DecisionResult<TState>
            {
                NextState = _currentState,
                IsCompleted = true,
                Reasoning = $"所有子引擎均无法决策: {string.Join(" → ", attemptedEngines)}"
            };
        }

        public void Reset()
        {
            _currentState = _initialState;
            foreach (var entry in _engines)
            {
                entry.Engine.Reset();
            }
        }

        public void SetCurrentState(TState state)
        {
            _currentState = state;
        }

        private static DecisionResult<TState> EnhanceReasoning(
            DecisionResult<TState> result,
            List<string> attemptedEngines)
        {
            if (attemptedEngines.Count <= 1)
                return result;

            var engineChain = string.Join(" → ", attemptedEngines);
            return result.WithReasoning($"引擎链 [{engineChain}]: {result.Reasoning}");
        }

        #region 内部类型

        private sealed record EngineEntry(
            IDecisionEngine<TState, TContext> Engine,
            bool Fallthrough);

        #endregion
    }

    #endregion
}
