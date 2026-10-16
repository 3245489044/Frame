using Strategy.Observability.Persistence;

namespace Strategy.Observability.Persistence;

/// <summary>
/// 内存事件区的「最近 N 条」回放面（E5 可选能力）：供实时出口（<c>Publish.LiveEventPipe</c>）在客户端刚连上时
/// 回放积压，避免「连上之前的窗口不可见」。实现方 = 有界内存环（<see cref="RingEventStore"/>）；落盘实现不实现本端口。
/// </summary>
public interface IRecentEvents
{
    /// <summary>最近 <paramref name="limit"/> 条（接收序 = 时间序；不足即全量）。</summary>
    IReadOnlyList<PersistedEvent> RecentSnapshot(int limit);
}
