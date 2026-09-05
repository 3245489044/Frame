namespace Modules.StateMachine
{
    #region 节点状态枚举

    public enum NodeStatus
    {
        Pending = 0,

        Running = 1,

        Completed = 2,

        Failed = 3,

        Skipped = 4,

        Paused = 5,

        Timeout = 6
    }

    #endregion
}
