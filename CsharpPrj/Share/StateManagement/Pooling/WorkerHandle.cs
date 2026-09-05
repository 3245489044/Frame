namespace Share.StateManagement.Pooling
{
    /// <summary>
    /// 传输层返回的受管 worker 句柄（纯数据载体）。
    /// 底层进程句柄/IPC 由传输层（如 ClineClient）持有，本框架仅记录状态与驱动转移。
    /// </summary>
    public sealed record WorkerHandle(string WorkerId, int Pid, DateTime StartedAtUtc);
}
