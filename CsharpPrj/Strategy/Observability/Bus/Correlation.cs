using Modules.Threading;

namespace Strategy.Observability.Bus;

/// <summary>关联键包（E4 作用域透传负载）：应用把 TraceId/SessionId/ScopeKey 放进来，Emit 自动捕获。</summary>
public sealed class CorrelationBag
{
    public string? TraceId { get; set; }
    public string? SessionId { get; set; }
    public string? ScopeKey { get; set; }

    public static CorrelationBag New(string? traceId = null, string? sessionId = null, string? scopeKey = null)
        => new() { TraceId = traceId, SessionId = sessionId, ScopeKey = scopeKey };
}

/// <summary>
/// 关联键 ambient 槽（复用 Modules.Threading.AmbientSlot<T>）：逻辑执行链内自动捕获，支持嵌套；
/// worker 边界不显式传入时得到默认 null（配合 FlowBoundary 切断）。
/// </summary>
public static class ObsScope
{
    private static readonly AmbientSlot<CorrelationBag> Slot = new("Strategy.Observability.Correlation");

    /// <summary>当前逻辑链上的关联键（无则 null）。</summary>
    public static CorrelationBag? Current => Slot.Current;

    /// <summary>进入作用域；Dispose 恢复先前值（支持嵌套）。</summary>
    public static IDisposable Enter(CorrelationBag bag)
    {
        ArgumentNullException.ThrowIfNull(bag);
        return Slot.Enter(bag);
    }
}
