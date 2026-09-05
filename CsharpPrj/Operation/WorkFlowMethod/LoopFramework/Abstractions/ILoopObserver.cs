using WorkFlowMethod.LoopFramework.Core;
using WorkFlowMethod.LoopFramework.StateMachine;

namespace WorkFlowMethod.LoopFramework.Abstractions
{
    public interface ILoopObserver
    {
        Task OnStateChangedAsync<TState>(TState oldState, TState newState);

        Task OnDecisionMadeAsync<TState>(StateTransition<TState> decision);

        Task OnActionExecutedAsync(ActionResult result);

        Task OnErrorAsync(Exception error, string context);
    }
}
