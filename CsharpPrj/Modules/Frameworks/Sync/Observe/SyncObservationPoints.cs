using Modules.Observe.Metrics;

namespace Modules.Sync.Observe;

/// <summary>
/// 同步协议 MVP 的 17 个观测点。
/// <para>口径源：<c>Docs/analysis/2026/11/11.5/2026-11-05-同步协议-MVP观测与拟合设计.md</c> §3
/// （规划文档 §8.2 的 11 项是旧口径，以 17 项为准）。</para>
/// <para>枚举顺序必须与 <see cref="SyncObservationPoints.All"/> 一一对应，且下标 = <c>MetricId</c> 下标 - 20。</para>
/// </summary>
public enum SyncObservationPoint : byte
{
    /// <summary>P01 一次完整握手的往返耗时。</summary>
    HandshakeRtt = 0,

    /// <summary>P02 失败握手次数。</summary>
    HandshakeFail = 1,

    /// <summary>P03 单条消息序列化耗时。</summary>
    EncodeTime = 2,

    /// <summary>P04 单条消息反序列化耗时。</summary>
    DecodeTime = 3,

    /// <summary>P05 每条消息的累计字节。</summary>
    PayloadBytes = 4,

    /// <summary>P06 单条消息字节数（保留 last/min/max）。</summary>
    MessageBytes = 5,

    /// <summary>P07 一个决策周期的状态提交耗时。</summary>
    CommitTime = 6,

    /// <summary>P08 成功提交次数。</summary>
    CommitCount = 7,

    /// <summary>P09 提交中止/回滚次数。</summary>
    CommitAbort = 8,

    /// <summary>P10 重传次数。</summary>
    RetransmitCount = 9,

    /// <summary>P11 窗口内 <c>retransmit / send</c>。</summary>
    RetransmitRatio = 10,

    /// <summary>P12 最近一次成功提交的窗口序号。</summary>
    WindowSeq = 11,

    /// <summary>P13 ACK 超时导致窗口未推进的次数。</summary>
    WindowStall = 12,

    /// <summary>P14 当前未确认消息数。</summary>
    InflightCount = 13,

    /// <summary>P15 心跳往返耗时。</summary>
    HeartbeatRtt = 14,

    /// <summary>P16 心跳超时次数。</summary>
    HeartbeatMiss = 15,

    /// <summary>P17 待发队列字节数。</summary>
    QueueBytes = 16,
}

/// <summary>
/// 单个观测点的静态元数据：<see cref="MetricId"/>、输出名、类型、单位、定点换算系数。
/// <para><see cref="Scale"/> 只用于 <c>Gauge</c>：<c>MetricProbe.Set</c> 只吃 <c>long</c>，
/// 比率类以「万分之一」定点存入，导出时除以 <see cref="SyncObservationPoints.RatioScale"/>。</para>
/// </summary>
public readonly record struct SyncObservationSpec(
    SyncObservationPoint Point,
    MetricId Id,
    string Name,
    MetricKind Kind,
    string Unit,
    double Scale);

/// <summary>
/// 17 个观测点与 <see cref="MetricId"/> 的<b>唯一</b>映射表。
/// <para>本表只消费 <c>Modules.Observe.Metrics</c>：枚举/名字/类型/<c>MetricCatalog.Count</c> 一律由 SY-00 独占，
/// 本目录<b>不得</b>新增 id、不得改 <c>MetricIds.cs</c>。</para>
/// <para>顺序为「追加不插队」：17 个 id 期望占用 <c>20..36</c>，<c>MetricCatalog.Count = 37</c>。</para>
/// </summary>
public static class SyncObservationPoints
{
    /// <summary>Gauge 定点比率的分母（万分之一）：0.5 存为 5000。</summary>
    public const double RatioScale = 10000.0;

    /// <summary><see cref="RatioScale"/> 的整型形式，供 <c>MetricProbe.Set</c> 使用。</summary>
    public const long RatioScaleLong = 10000;

    /// <summary>17 个观测点的期望 <c>MetricId</c> 起始下标（SY-00 追加段的第一个槽位）。</summary>
    public const int FirstIndex = 20;

    /// <summary>17 个观测点全部落地后的 <c>MetricCatalog.Count</c>。</summary>
    public static readonly int ExpectedCatalogCount = FirstIndex + 17;

    /// <summary>按 <see cref="SyncObservationPoint"/> 顺序排列的元数据表。</summary>
    public static readonly IReadOnlyList<SyncObservationSpec> All = new SyncObservationSpec[]
    {
        new(SyncObservationPoint.HandshakeRtt, MetricId.SyncHandshakeRtt,
            "sync.handshake.rtt", MetricKind.Histogram, "ms", 1),
        new(SyncObservationPoint.HandshakeFail, MetricId.SyncHandshakeFail,
            "sync.handshake.fail", MetricKind.Counter, "count", 1),
        new(SyncObservationPoint.EncodeTime, MetricId.SyncEncodeTime,
            "sync.encode.time", MetricKind.Histogram, "ms", 1),
        new(SyncObservationPoint.DecodeTime, MetricId.SyncDecodeTime,
            "sync.decode.time", MetricKind.Histogram, "ms", 1),
        new(SyncObservationPoint.PayloadBytes, MetricId.SyncPayloadBytes,
            "sync.payload.bytes", MetricKind.Counter, "byte", 1),
        new(SyncObservationPoint.MessageBytes, MetricId.SyncMessageBytes,
            "sync.message.bytes", MetricKind.Gauge, "byte", 1),
        new(SyncObservationPoint.CommitTime, MetricId.SyncCommitTime,
            "sync.commit.time", MetricKind.Histogram, "ms", 1),
        new(SyncObservationPoint.CommitCount, MetricId.SyncCommitCount,
            "sync.commit.count", MetricKind.Counter, "count", 1),
        new(SyncObservationPoint.CommitAbort, MetricId.SyncCommitAbort,
            "sync.commit.abort", MetricKind.Counter, "count", 1),
        new(SyncObservationPoint.RetransmitCount, MetricId.SyncRetransmitCount,
            "sync.retransmit.count", MetricKind.Counter, "count", 1),
        new(SyncObservationPoint.RetransmitRatio, MetricId.SyncRetransmitRatio,
            "sync.retransmit.ratio", MetricKind.Gauge, "ratio", RatioScale),
        new(SyncObservationPoint.WindowSeq, MetricId.SyncWindowSeq,
            "sync.window.seq", MetricKind.Gauge, "seq", 1),
        new(SyncObservationPoint.WindowStall, MetricId.SyncWindowStall,
            "sync.window.stall", MetricKind.Counter, "count", 1),
        new(SyncObservationPoint.InflightCount, MetricId.SyncInflightCount,
            "sync.inflight.count", MetricKind.Gauge, "count", 1),
        new(SyncObservationPoint.HeartbeatRtt, MetricId.SyncHeartbeatRtt,
            "sync.heartbeat.rtt", MetricKind.Histogram, "ms", 1),
        new(SyncObservationPoint.HeartbeatMiss, MetricId.SyncHeartbeatMiss,
            "sync.heartbeat.miss", MetricKind.Counter, "count", 1),
        new(SyncObservationPoint.QueueBytes, MetricId.SyncQueueBytes,
            "sync.queue.bytes", MetricKind.Gauge, "byte", 1),
    };

    public static int Count => All.Count;

    public static SyncObservationSpec Spec(SyncObservationPoint point)
    {
        int index = (int)point;
        return (uint)index < (uint)All.Count
            ? All[index]
            : throw new ArgumentOutOfRangeException(nameof(point), point, "未知观测点。");
    }

    public static string NameOf(SyncObservationPoint point) => Spec(point).Name;

    /// <summary>
    /// 目录自检：名字/类型/下标与 <c>MetricCatalog</c> 对拍，防「id 未落地 / 追加错位 / Count 与数组长度不一」。
    /// <para>只读，不改表；调用点在宿主启动时一次（返回 false 时宿主以退出码 2 终止，避免运行期越界）。</para>
    /// </summary>
    public static bool TryValidateCatalog(out string error)
    {
        if (MetricCatalog.Count < ExpectedCatalogCount)
        {
            error = $"MetricCatalog.Count={MetricCatalog.Count} < {ExpectedCatalogCount}（17 个 sync.* id 未落地或未改 Count）";
            return false;
        }

        for (int i = 0; i < All.Count; i++)
        {
            SyncObservationSpec spec = All[i];
            if ((int)spec.Id != FirstIndex + i)
            {
                error = $"{spec.Name} 的 MetricId 下标应为 {FirstIndex + i}，实际 {(int)spec.Id}（追加段必须紧接 20 且顺序一致）";
                return false;
            }

            if (MetricCatalog.NameOf(spec.Id) != spec.Name)
            {
                error = $"{spec.Name} 的名字表不一致：MetricCatalog.NameOf={MetricCatalog.NameOf(spec.Id)}";
                return false;
            }

            if (MetricCatalog.KindOf(spec.Id) != spec.Kind)
            {
                error = $"{spec.Name} 的类型表不一致：期望 {spec.Kind}，实际 {MetricCatalog.KindOf(spec.Id)}";
                return false;
            }
        }

        error = string.Empty;
        return true;
    }
}
