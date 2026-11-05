namespace Modules.Sync.Contracts;

/// <summary>
/// 进程内传输端口：只做两件事——入队/出队 + 容量上限；<b>不解释消息语义</b>、不做重排、不自动重发。
/// <para>违约表现：若传输层把“满队”改成无界增长，<c>sync.retransmit</c> 与 <c>sync.inflight</c> 的下降段消失，压力测试断言失败。</para>
/// </summary>
public interface ISyncTransport
{
    /// <summary>单方向容量上限（队列满时 <see cref="Send"/> 返回 false）。</summary>
    int Capacity { get; }

    /// <summary>当前排队条数。</summary>
    int QueueDepth { get; }

    /// <summary>入队；队列满返回 false（调用方自行计入重发/丢弃，禁止阻塞）。</summary>
    bool Send(SyncMessageKind kind, object payload);

    /// <summary>出队；空队返回 false。</summary>
    bool TryReceive(out SyncMessageKind kind, out object? payload);
}

/// <summary>
/// 权威端端口：只暴露 tick 推进、输入应用与快照构建；策略/规则表/伤害公式一律不进入接口。
/// <para>违约表现：若任一实现把“决策 tick”当成“模拟 tick”，快照 tick 间隔与 <c>sync.applyLatency</c> 基线立刻偏离。</para>
/// </summary>
public interface ISyncAuthority
{
    /// <summary>当前决策 tick。</summary>
    long DecisionTick { get; }

    /// <summary>推进恰好一个决策 tick；内部按契约 tick 顺序执行。</summary>
    void AdvanceOneTick();

    /// <summary>应用一条已通过窗口校验与幂等校验的接管命令。</summary>
    void ApplyInput(TakeoverCommand command);

    /// <summary>构建当前决策 tick 的完整快照（投影 + 哈希）。</summary>
    Snapshot BuildSnapshot();
}

/// <summary>
/// 客户端端口：只暴露建会话、应用快照、入队命令；不暴露渲染态与输入采集。
/// <para>违约表现：若实现自行“修补”快照字段，客户端重算哈希与权威端不相等，<c>sync.applyLatency</c> 有值但 <c>sync.hashMismatch</c> 同步增长。</para>
/// </summary>
public interface ISyncReplica
{
    /// <summary>按 Welcome 完成建会话；版本/规则集不符时必须走 Reject 路径而不是继续。</summary>
    void Connect(Welcome welcome);

    /// <summary>应用快照并返回是否被接受（重复 tick、过期 tick、哈希不符均返回 false）。</summary>
    bool ApplySnapshot(Snapshot snapshot);

    /// <summary>入队一条接管命令；命令 id 由调用方分配，重试必须复用同一个 id。</summary>
    void EnqueueCommand(TakeoverCommand command);
}
