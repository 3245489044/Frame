namespace Modules.Sync.Contracts;

/// <summary>
/// 拒绝/错误原因：v1 只认这 6 种。<b>追加只能到末尾</b>（编号即线上字节）。
/// <para>每条拒绝都必须能被观测到（<c>sync.handshakeFail</c> 或 <c>sync.commitAbort</c> 的 tag），
/// 否则就是“静默失败”，不算契约完备。</para>
/// </summary>
public enum RejectReason : byte
{
    /// <summary>协议版本不一致（<see cref="Hello.ProtocolVersion"/> ≠ <see cref="SyncProtocol.ProtocolVersion"/>）。</summary>
    VersionMismatch = 0,

    /// <summary>规则集哈希不一致（两端不是同一套规则）。</summary>
    RulesetMismatch = 1,

    /// <summary>畸形载荷（字段错位、长度不足、字符串超限、未知消息类型）。</summary>
    Malformed = 2,

    /// <summary>目标 tick 已过（输入迟到，窗口外）。</summary>
    Late = 3,

    /// <summary>目标 tick 在未来窗口外（输入太早）。</summary>
    Future = 4,

    /// <summary>重复命令（幂等键 <c>(TargetTick, CommandId)</c> 已处理过）。</summary>
    Duplicate = 5,

    /// <summary>
    /// 逐帧校验和失配（稳定名 <c>checksum-mismatch</c>）；该帧必须整帧拒收、不得提交。
    /// <para>当前为契约缺口的临时取值，待契约确认后调整。</para>
    /// </summary>
    ChecksumMismatch = 6,
}

/// <summary>
/// 观测端口：协议内核只向上报事件，不直接读指标库、不写日志字段。
/// <para>
/// 违约表现：并发任务若绕过本端口自行打点，指标 id/单位/标签会分裂，<c>MetricCatalog</c> 对拍立刻不一致；
/// 因此所有上行点都必须落在 <see cref="OnHandshakeCompleted"/> 等 9 个回调之一。
/// </para>
/// </summary>
public abstract class SyncObserver
{
    /// <summary>握手成功（对端版本与规则集哈希均匹配）。</summary>
    public virtual void OnHandshakeCompleted(long sessionId) { }

    /// <summary>握手失败/建会话被拒。</summary>
    public virtual void OnHandshakeFailed(RejectReason reason) { }

    /// <summary>一个决策 tick 求值完成；<paramref name="ruleCount"/> 为短路后实际求值条数。</summary>
    public virtual void OnTickEvaluated(long tick, int ruleCount) { }

    /// <summary>一个决策 tick 中止（未产出快照）。</summary>
    public virtual void OnTickAborted(long tick) { }

    /// <summary>快照下发完成。</summary>
    public virtual void OnSnapshotSent(long tick, int payloadBytes) { }

    /// <summary>快照在客户端成功应用。</summary>
    public virtual void OnSnapshotApplied(long tick) { }

    /// <summary>快照被客户端丢弃（重复/过期/畸形/哈希不符）。</summary>
    public virtual void OnSnapshotDiscarded(long tick) { }

    /// <summary>输入或被接管被拒（含窗口外与重复）。</summary>
    public virtual void OnInputRejected(RejectReason reason) { }

    /// <summary>双端状态哈希在 <paramref name="tick"/> 不一致。</summary>
    public virtual void OnStateMismatch(long tick) { }
}

/// <summary>空观察者：不接观测也能跑闭环（自带默认实现，避免宿主被迫传 null）。</summary>
public sealed class NoopSyncObserver : SyncObserver
{
}
