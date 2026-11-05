using Share.TaskStatusPool.TaskPool;

namespace Share.Signal;

/// <summary>信号账本载体编解码端口：把框架状态记录适配到宿主任务池条目。</summary>
public interface ISignalLedgerCodec<TRecord>
    where TRecord : ITaskPoolItem
{
    /// <summary>读取条目主键。</summary>
    string TaskKey(TRecord record);

    /// <summary>读取持久化载荷文本。</summary>
    string ReadPayload(TRecord record);

    /// <summary>写回持久化载荷文本。</summary>
    void WritePayload(TRecord record, string payload);

    /// <summary>构造新账本条目（<paramref name="taskKey"/> = 账本键）。</summary>
    TRecord Create(string taskKey, string payload, SignalEnvelope envelope, int priority, string executorId);
}

/// <summary>信号账本选项（构造注入，禁静态配置读取）。</summary>
public sealed record SignalLedgerOptions(
    int MaxAttempts = 3,
    TimeSpan? LeaseTtl = null,
    TimeSpan? QueuedTtl = null,
    TimeSpan? TerminalTtl = null,
    string SignalExecutorId = "SignalFlush",
    int ScanLimit = 1000)
{
    public long LeaseTtlMs => (long)(LeaseTtl ?? TimeSpan.FromSeconds(120)).TotalMilliseconds;
    public long QueuedTtlMs => (long)(QueuedTtl ?? TimeSpan.FromSeconds(300)).TotalMilliseconds;
    public long TerminalTtlMs => (long)(TerminalTtl ?? TimeSpan.FromSeconds(3600)).TotalMilliseconds;
}
