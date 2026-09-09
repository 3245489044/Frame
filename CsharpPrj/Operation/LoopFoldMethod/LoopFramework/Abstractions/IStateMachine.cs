using WorkFlowMethod.LoopFramework.StateMachine;

namespace WorkFlowMethod.LoopFramework.Abstractions
{
    public interface IStateMachine<TState>
    {
        TState CurrentState { get; }

        string Name { get; }

        Task<StateTransition<TState>> DecideAsync<TContext>(
            TContext context,
            CancellationToken cancellationToken = default);

        void Reset();
    }
}
