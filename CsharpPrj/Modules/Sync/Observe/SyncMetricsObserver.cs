using System.Diagnostics;
using Modules.Observe.Metrics;
using Modules.Sync.Contracts;

namespace Modules.Sync.Observe;

/// <summary>
/// <c>SyncObserver</c> 端口 → <c>Modules.Observe.Metrics</c> 的适配实现。
/// <para><b>唯一映射点</b>：<c>MetricId</c> 字面量只出现在本文件与 <see cref="SyncObservationPoints"/>；
/// 协议内核（Transport / Session / Replica）与玩法内核只调端口回调，不引用 <c>MetricId</c>（观测设计 §8.1）。</para>
/// <para><b>关闭态</b>（<c>ENGINE_METRICS</c> 未设）：<c>Metrics.*</c> 返回 <c>default</c> 句柄，首行短路，
/// 不分配、不拼字符串；本类只在关闭态下做整型自增与一次 null 判断。</para>
/// <para><b>计时类</b>（P01/P03/P04/P07/P15）：<c>Metrics.Timer</c> 只能量自身词法作用域，
/// 故由调用方（宿主 / 会话 / 编解码边界）取 <see cref="SyncMetricsObserver.BeginHandshake"/> 等作用域；
/// 作用域同时向 Metrics 与 <see cref="ISyncObservationSink"/> 出数，保证两条通道用同一时间基。</para>
/// <para><b>诊断计数</b>（R2 旧快照 / R3 迟到·超前瞻 / R4 重复 / R6 哈希不一致）：17 点指标表<b>不含</b>对应 id，
/// 故只在本类保留本地计数，供宿主摘要行；不写 Metrics、不参与正确性判定。</para>
/// </summary>
/// <remarks>
/// 契约假设（SY-00 冻结后若签名不同，只改本文件的 override 签名，不动 #17 点映射）：
/// <c>SyncObserver</c> 为抽象基类，回调 9 个：
/// <c>OnHandshakeCompleted(long sessionId)</c>、<c>OnHandshakeFailed(RejectReason)</c>、
/// <c>OnTickEvaluated(long tick, int ruleCount)</c>、<c>OnTickAborted(long tick)</c>、
/// <c>OnSnapshotSent(long tick, int payloadBytes)</c>、<c>OnSnapshotApplied(long tick)</c>、
/// <c>OnSnapshotDiscarded(long tick)</c>、<c>OnInputRejected(RejectReason)</c>、<c>OnStateMismatch(long tick)</c>；
/// <c>RejectReason</c> 取值含 <c>Late / Future / Duplicate</c>。
/// </remarks>
public sealed class SyncMetricsObserver : SyncObserver
{
    private readonly ISyncObservationSink? _sink;

    private long _snapshotStale;
    private long _inputLate;
    private long _inputFuture;
    private long _inputDuplicate;
    private long _hashMismatch;

    public SyncMetricsObserver(ISyncObservationSink? sink = null) => _sink = sink;

    // ── 计时作用域（调用方 using）────────────────────────────────────────

    /// <summary>P01：包住 <c>Hello</c> 入队 → <c>Welcome</c> 出队。</summary>
    public SyncObservationScope BeginHandshake() =>
        Begin(SyncObservationPoint.HandshakeRtt, MetricId.SyncHandshakeRtt);

    /// <summary>P03：包住纯编解码边界（结构 → 字节），不含排队等待。</summary>
    public SyncObservationScope BeginEncode() =>
        Begin(SyncObservationPoint.EncodeTime, MetricId.SyncEncodeTime);

    /// <summary>P04：包住纯编解码边界（字节 → 结构），不含排队等待。</summary>
    public SyncObservationScope BeginDecode() =>
        Begin(SyncObservationPoint.DecodeTime, MetricId.SyncDecodeTime);

    /// <summary>P07：包住一个决策周期的状态提交事务（进入提交 → 状态哈希更新完成）。</summary>
    public SyncObservationScope BeginCommit() =>
        Begin(SyncObservationPoint.CommitTime, MetricId.SyncCommitTime);

    /// <summary>P15：包住心跳发出 → 心跳回包。</summary>
    public SyncObservationScope BeginHeartbeat() =>
        Begin(SyncObservationPoint.HeartbeatRtt, MetricId.SyncHeartbeatRtt);

    // ── 端口回调 ───────────────────────────────────────────────────────

    /// <summary>P01 由 <see cref="BeginHandshake"/> 计时，此处只用握手成功做存在性标记。</summary>
    public override void OnHandshakeCompleted(long sessionId)
    {
    }

    /// <summary>P02：失败握手 +1（版本 / 规则集 / 畸形 / 超时）。</summary>
    public override void OnHandshakeFailed(RejectReason reason)
    {
        Metrics.Counter(MetricId.SyncHandshakeFail).Add(1);
        _sink?.Sample(SyncObservationPoint.HandshakeFail, 1);
    }

    /// <summary>P08：成功提交 +1；P07 由 <see cref="BeginCommit"/> 计时。</summary>
    public override void OnTickEvaluated(long tick, int ruleCount)
    {
        Metrics.Counter(MetricId.SyncCommitCount).Add(1);
        _sink?.Sample(SyncObservationPoint.CommitCount, 1);
    }

    /// <summary>P09：提交中止 +1。</summary>
    public override void OnTickAborted(long tick)
    {
        Metrics.Counter(MetricId.SyncCommitAbort).Add(1);
        _sink?.Sample(SyncObservationPoint.CommitAbort, 1);
    }

    /// <summary>P05 累计字节 + P06 单条字节（Gauge last/min/max）。</summary>
    public override void OnSnapshotSent(long tick, int payloadBytes)
    {
        Metrics.Gauge(MetricId.SyncMessageBytes).Set(payloadBytes);
        Metrics.Counter(MetricId.SyncPayloadBytes).Add(payloadBytes);
        if (_sink is null) return;
        _sink.Sample(SyncObservationPoint.MessageBytes, payloadBytes);
        _sink.Sample(SyncObservationPoint.PayloadBytes, payloadBytes);
    }

    /// <summary>P12：最近一次成功提交的窗口序号（MVP 以决策 tick 为窗口序）。</summary>
    public override void OnSnapshotApplied(long tick)
    {
        Metrics.Gauge(MetricId.SyncWindowSeq).Set(tick);
        _sink?.Sample(SyncObservationPoint.WindowSeq, tick);
    }

    /// <summary>R2 旧/重复快照：17 点表无对应 id，只记宿主诊断计数。</summary>
    public override void OnSnapshotDiscarded(long tick) => _snapshotStale++;

    /// <summary>R3/R4：17 点表无对应 id，只记宿主诊断计数。</summary>
    public override void OnInputRejected(RejectReason reason)
    {
        switch (reason)
        {
            case RejectReason.Late:
                _inputLate++;
                break;
            case RejectReason.Future:
                _inputFuture++;
                break;
            case RejectReason.Duplicate:
                _inputDuplicate++;
                break;
        }
    }

    /// <summary>R6 哈希不一致：17 点表无对应 id，只记宿主诊断计数。</summary>
    public override void OnStateMismatch(long tick) => _hashMismatch++;

    // ── 非回调类观测点（由宿主 / 会话在明确边界调用）────────────────────

    /// <summary>P10：重传 +1。</summary>
    public void RecordRetransmit()
    {
        Metrics.Counter(MetricId.SyncRetransmitCount).Add(1);
        _sink?.Sample(SyncObservationPoint.RetransmitCount, 1);
    }

    /// <summary>P13：ACK 超时导致窗口未推进 +1。</summary>
    public void RecordWindowStall()
    {
        Metrics.Counter(MetricId.SyncWindowStall).Add(1);
        _sink?.Sample(SyncObservationPoint.WindowStall, 1);
    }

    /// <summary>P16：心跳超时 +1。</summary>
    public void RecordHeartbeatMiss()
    {
        Metrics.Counter(MetricId.SyncHeartbeatMiss).Add(1);
        _sink?.Sample(SyncObservationPoint.HeartbeatMiss, 1);
    }

    /// <summary>P14：当前未确认消息数。</summary>
    public void RecordInflight(long pendingMessages)
    {
        Metrics.Gauge(MetricId.SyncInflightCount).Set(pendingMessages);
        _sink?.Sample(SyncObservationPoint.InflightCount, pendingMessages);
    }

    /// <summary>P17：待发队列字节数。字节口径待契约提供（<c>SyncMessage</c> 无尺寸访问器前由宿主传 0 以外的已知值）。</summary>
    public void RecordQueueBytes(long pendingBytes)
    {
        Metrics.Gauge(MetricId.SyncQueueBytes).Set(pendingBytes);
        _sink?.Sample(SyncObservationPoint.QueueBytes, pendingBytes);
    }

    /// <summary>
    /// P11：窗口末结算 <c>retransmit / send</c>。
    /// <para>分母为 0 时<b>不写</para>（FG13：0/0 不得落成 0），由读取端按「不可判」处理。</para>
    /// </summary>
    public void EndWindow(long sentCount, long retransmitCount)
    {
        if (sentCount <= 0) return;

        long perMyriad = retransmitCount * SyncObservationPoints.RatioScaleLong / sentCount;
        Metrics.Gauge(MetricId.SyncRetransmitRatio).Set(perMyriad);
        _sink?.Sample(SyncObservationPoint.RetransmitRatio, (double)retransmitCount / sentCount);
    }

    // ── 诊断计数只读出口（宿主摘要行用，不进 Metrics）────────────────────

    public long SnapshotStale => Volatile.Read(ref _snapshotStale);

    public long InputLate => Volatile.Read(ref _inputLate);

    public long InputFuture => Volatile.Read(ref _inputFuture);

    public long InputDuplicate => Volatile.Read(ref _inputDuplicate);

    public long HashMismatch => Volatile.Read(ref _hashMismatch);

    // ── 内部 ───────────────────────────────────────────────────────────

    private SyncObservationScope Begin(SyncObservationPoint point, MetricId id) =>
        new(Metrics.Timer(id), _sink, point, Stopwatch.GetTimestamp());
}

/// <summary>
/// 计时作用域：Dispose 时向 Metrics 记一次 Histogram（与 <c>Metrics.Timer</c> 同一时间基近似），
/// 同时把精确毫秒喂给 <see cref="ISyncObservationSink"/>。关闭态下两个通道都短路。
/// </summary>
public readonly struct SyncObservationScope : IDisposable
{
    private readonly MetricScope _scope;
    private readonly ISyncObservationSink? _sink;
    private readonly SyncObservationPoint _point;
    private readonly long _start;

    internal SyncObservationScope(MetricScope scope, ISyncObservationSink? sink, SyncObservationPoint point, long start)
    {
        _scope = scope;
        _sink = sink;
        _point = point;
        _start = start;
    }

    public void Dispose()
    {
        long elapsedTicks = Stopwatch.GetTimestamp() - _start;
        _scope.Dispose(); // Histogram（桶上界口径，只做守门）

        if (_sink is null) return;
        _sink.Sample(_point, elapsedTicks * 1000.0 / Stopwatch.Frequency);
    }
}
