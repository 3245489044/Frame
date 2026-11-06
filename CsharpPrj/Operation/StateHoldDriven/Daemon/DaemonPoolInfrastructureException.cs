namespace Operation.StateBlock;

/// <summary>
/// daemon 池基建异常（进程/传输/池容量问题），与 <see cref="DaemonPoolException"/>（业务执行失败）区分。
/// <para>调用方据此决定是否归因为“环境不可用”，而不是任务失败；避免把所有失败压成同一个业务异常（E-7）。</para>
/// </summary>
public sealed class DaemonPoolInfrastructureException : Exception
{
    public DaemonPoolInfrastructureException(string message) : base(message) { }

    public DaemonPoolInfrastructureException(string message, Exception inner) : base(message, inner) { }
}
