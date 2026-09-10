namespace Share.StateManagement.Pooling
{
    /// <summary>池启动结果。<see cref="Workers"/> 为成功 spawn 的句柄；失败时 <see cref="Error"/> 承载原因。</summary>
    public sealed record PoolInitResult(
        bool Succeeded,
        int StartedCount,
        IReadOnlyList<WorkerHandle> Workers,
        string? Error = null);
}
