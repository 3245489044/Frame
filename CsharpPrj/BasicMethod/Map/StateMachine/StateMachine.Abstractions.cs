using System;
using System.Collections.Generic;

namespace Modules.StateMachine
{
    #region 状态变更事件

    public class StateChangedEventArgs<TState> : EventArgs
    {
        public TState FromState { get; }

        public TState ToState { get; }

        public string? Operation { get; }

        public DateTime Timestamp { get; }

        public StateChangedEventArgs(TState fromState, TState toState, string? operation = null)
        {
            FromState = fromState;
            ToState = toState;
            Operation = operation;
            Timestamp = DateTime.UtcNow;
        }
    }

    #endregion

    #region 状态机接口

    public interface IStateMachine<TState>
    {
        TState CurrentState { get; }

        event EventHandler<StateChangedEventArgs<TState>>? StateChanged;

        void Reset(TState initialState);
    }

    public interface IConditionalStateMachine<TState> : IStateMachine<TState>
        where TState : struct, Enum
    {
        DateTime StartTime { get; }

        DateTime? EndTime { get; }

        bool IsActive { get; }

        bool IsFinished { get; }

        bool TryTransitionTo(TState newState, Func<TState, bool>? condition = null);

        bool TryTransitionFrom(TState expectedState, TState newState);
    }

    public interface IStateMachineStore<TState>
        where TState : struct, Enum
    {
        IConditionalStateMachine<TState> GetOrCreate(string id);

        IConditionalStateMachine<TState>? Get(string id);

        bool Remove(string id);

        IEnumerable<string> GetActiveIds();

        IEnumerable<string> GetAllIds();

        int CleanupFinished();

        int Count { get; }

        int MaxCapacity { get; set; }

        TimeSpan StateRetentionPeriod { get; set; }
    }

    public interface IPauseHandle
    {
        bool IsPaused { get; }

        void Pause();

        void Resume();

        Task WaitIfPausedAsync(CancellationToken cancellationToken);
    }

    public interface IPauseManager
    {
        bool IsGloballyPaused { get; }

        void RequestGlobalPause();

        void ResumeGlobalPause();

        bool IsLocallyPaused(string scopeId);

        void RequestLocalPause(string scopeId);

        void ResumeLocalPause(string scopeId);

        Task WaitForResumeAsync(string? scopeId, CancellationToken cancellationToken, int pollIntervalMs = 100);
    }

    public interface IScopeStateManager<TState>
        where TState : struct, Enum
    {
        IConditionalStateMachine<TState> Global { get; }

        IConditionalStateMachine<TState> CreateScope(string scopeName);

        IConditionalStateMachine<TState>? GetScope(string scopeName);

        bool ReleaseScope(string scopeName);

        IReadOnlyCollection<string> GetScopeNames();
    }

    #endregion

    #region 异常定义

    public class InvalidTransitionException : InvalidOperationException
    {
        public object? FromState { get; }

        public string? Operation { get; }

        public InvalidTransitionException() { }

        public InvalidTransitionException(string message) : base(message) { }

        public InvalidTransitionException(string message, Exception inner) : base(message, inner) { }

        public InvalidTransitionException(object fromState, string operation)
            : base($"状态转换非法: 当前状态 '{fromState}' 不允许操作 '{operation}'")
        {
            FromState = fromState;
            Operation = operation;
        }
    }

    public class StateMachineConfigurationException : InvalidOperationException
    {
        public StateMachineConfigurationException() { }

        public StateMachineConfigurationException(string message) : base(message) { }

        public StateMachineConfigurationException(string message, Exception inner) : base(message, inner) { }
    }

    #endregion
}
