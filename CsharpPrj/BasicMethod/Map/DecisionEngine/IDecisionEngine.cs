namespace BasicMethod.DecisionEngine
{
    #region 决策引擎核心接口

    public interface IDecisionEngine<TState, TContext>
    {
        string Name { get; }

        TState CurrentState { get; }

        Task<DecisionResult<TState>> DecideAsync(TContext context, CancellationToken cancellationToken = default);

        void SetCurrentState(TState state);

        void Reset();
    }

    #endregion

    #region 决策结果

    public class DecisionResult<TState>
    {
        public TState NextState { get; init; } = default!;

        public string? ActionName { get; init; }

        public string Reasoning { get; init; } = string.Empty;

        public bool IsCompleted { get; init; }

        public bool IsDeterministic { get; init; } = true;

        public double Confidence { get; init; } = 1.0;

        public DecisionResult<TState> WithReasoning(string reasoning) => new()
        {
            NextState = NextState,
            ActionName = ActionName,
            Reasoning = reasoning,
            IsCompleted = IsCompleted,
            IsDeterministic = IsDeterministic,
            Confidence = Confidence
        };
    }

    #endregion

    #region 决策规则

    public class DecisionRule<TState, TContext>
    {
        public string Name { get; init; } = string.Empty;

        public TState FromState { get; init; } = default!;

        public TState ToState { get; init; } = default!;

        public string? ActionName { get; init; }

        public Func<TContext, bool>? Condition { get; init; }

        public string? ConditionExpression { get; init; }

        public int Priority { get; init; }

        public string Description { get; init; } = string.Empty;
    }

    #endregion

    #region 异常定义

    public class DecisionEngineConfigurationException : InvalidOperationException
    {
        public DecisionEngineConfigurationException() { }
        public DecisionEngineConfigurationException(string message) : base(message) { }
        public DecisionEngineConfigurationException(string message, Exception inner) : base(message, inner) { }
    }

    public class DecisionExecutionException : InvalidOperationException
    {
        public string? EngineName { get; }
        public object? CurrentState { get; }

        public DecisionExecutionException() { }
        public DecisionExecutionException(string message) : base(message) { }
        public DecisionExecutionException(string message, Exception inner) : base(message, inner) { }

        public DecisionExecutionException(string engineName, object currentState, string message)
            : base($"决策引擎 '{engineName}' 在状态 '{currentState}' 下执行失败: {message}")
        {
            EngineName = engineName;
            CurrentState = currentState;
        }
    }

    #endregion
}
