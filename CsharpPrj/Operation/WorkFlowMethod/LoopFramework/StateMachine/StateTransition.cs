namespace WorkFlowMethod.LoopFramework.StateMachine
{
    public class StateTransition<TState>
    {
        public TState NextState { get; set; }

        public string ActionName { get; set; }

        public string Reasoning { get; set; }

        public bool IsCompleted { get; set; }

        public bool IsLLMDecision { get; set; }
    }
}
