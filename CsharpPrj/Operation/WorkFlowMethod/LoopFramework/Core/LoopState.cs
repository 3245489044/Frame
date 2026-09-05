namespace WorkFlowMethod.LoopFramework.Core
{
    public enum LoopState
    {
        NotStarted,

        Running,

        Deciding,

        Paused,

        Completed,

        Failed,

        Terminated,

        MaxIterationsReached
    }
}
