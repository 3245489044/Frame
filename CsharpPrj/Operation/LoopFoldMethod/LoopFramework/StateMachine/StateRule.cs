using WorkFlowMethod.LoopFramework.Core;

namespace WorkFlowMethod.LoopFramework.StateMachine
{
    public class StateRule
    {
        public string Name { get; set; }

        public LoopState FromState { get; set; }

        public LoopState ToState { get; set; }

        public string ActionName { get; set; }

        public string Condition { get; set; }

        public int Priority { get; set; } = 0;

        public string Description { get; set; }
    }
}
