using Strategy.Observability.Entity;

namespace Strategy.Observability.Persistence;

/// <summary>
/// 事件窗口过滤单一实现（E5 下篇）：<c>scope + afterSeq + EventSeq 升序 + limit</c> 的唯一判据，
/// InMemory / Ring / File 三处事件区共用，禁止各自再写一份。
/// <para>
/// <c>limit</c> 语义（三处统一）：<c>&gt;0</c> = 本次窗口上限；<c>0</c> / 负数 = 返回空集
/// （负数不解释为「不限」，需要不限请传 <see cref="int.MaxValue"/>）。
/// </para>
/// </summary>
internal static class EventWindow
{
    /// <summary>窗口条数上限归一：负数按 0 处理。</summary>
    internal static int EffectiveLimit(int limit) => limit < 0 ? 0 : limit;

    /// <summary>按作用域 + 续读位点过滤并按 EventSeq 升序取前 <paramref name="limit"/> 条。</summary>
    internal static IEnumerable<PersistedEvent> Timeline(
        IEnumerable<PersistedEvent> source, string scopeId, long? afterSeq, int limit)
    {
        var remaining = EffectiveLimit(limit);
        if (remaining == 0) return Array.Empty<PersistedEvent>();

        return source
            .Where(e => string.Equals(e.ScopeId, scopeId, StringComparison.Ordinal))
            .Where(e => !afterSeq.HasValue || e.EventSeq > afterSeq.Value)
            .OrderBy(e => e.EventSeq)
            .Take(remaining);
    }

    /// <summary>同一实体（三元全等）的事件链：作用域过滤 + 升序（判据 = <see cref="EntityId.Matches"/>）。</summary>
    internal static IEnumerable<PersistedEvent> EntityChain(
        IEnumerable<PersistedEvent> source, EntityId entity)
        => source
            .Where(e => string.Equals(e.ScopeId, entity.ScopeId, StringComparison.Ordinal))
            .Where(e => e.Event.Entity.Matches(entity))
            .OrderBy(e => e.EventSeq);

    /// <summary>
    /// 尾帧 <c>truncated</c>：本次窗口是否被环形边界截断。
    /// <para>
    /// 环按全局容量丢弃最旧行，不按作用域记账，因此只有「从起点读（afterSeq 缺省）且环已丢过行」可判定为真；
    /// 给定 <paramref name="afterSeq"/> 的续读窗口无法区分「稀疏」与「已丢弃」，按未截断报告。
    /// 生命周期累计丢弃量另见 <c>droppedTotal</c>（不做窗口判定）。
    /// </para>
    /// </summary>
    internal static bool WindowTruncated(long? afterSeq, long droppedTotal)
        => droppedTotal > 0 && !(afterSeq is > 0);
}
