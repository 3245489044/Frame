namespace Modules.StateMachine
{
    #region 执行状态枚举

    public enum ExecutionStatus
    {
        Pending = 0,

        Running = 1,

        Completed = 2,

        Failed = 3,

        Cancelled = 4,

        Paused = 5,

        Timeout = 6
    }

    #endregion
}
