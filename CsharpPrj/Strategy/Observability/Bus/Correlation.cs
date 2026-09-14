using Modules.Threading;

namespace Strategy.Observability.Bus;

/// <summary>关联键包（E4 作用域透传负载）：应用把关联键放进来，Emit 自动捕获并落行键。</summary>
public sealed class CorrelationBag
{
    public string? TraceId { get; set; }
    public string? SessionId { get; set; }
    public string? ScopeKey { get; set; }

    /// <summary>运行标识（跨轮聚合的稳定维度；G4 补全）。</summary>
    public string? RunId { get; set; }

    /// <summary>轮次标识（约定形如 <c>{runId}R{n}</c>；G4 补全）。</summary>
    public string? RoundId { get; set; }

    /// <summary>构造关联键包（原签名；保留以维持既有烘焙消费方的二进制兼容）。</summary>
    public static CorrelationBag New(string? traceId = null, string? sessionId = null, string? scopeKey = null)
        => new() { TraceId = traceId, SessionId = sessionId, ScopeKey = scopeKey };

    /// <summary>构造关联键包（含运行/轮次维度的全量签名；五参全给，避免与三参重载歧义）。</summary>
    public static CorrelationBag New(string? traceId, string? sessionId, string? scopeKey, string? runId, string? roundId)
        => new() { TraceId = traceId, SessionId = sessionId, ScopeKey = scopeKey, RunId = runId, RoundId = roundId };

    /// <summary>运行作用域构造（命名参数友好；轮次标识约定 <c>{runId}R{n}</c>）。</summary>
    public static CorrelationBag Run(
        string runId,
        string? roundId = null,
        string? traceId = null,
        string? sessionId = null,
        string? scopeKey = null)
        => new()
        {
            TraceId = traceId,
            SessionId = sessionId,
            ScopeKey = scopeKey,
            RunId = runId,
            RoundId = roundId,
        };
}

/// <summary>
/// 关联键 ambient 槽（复用 Modules.Threading.AmbientSlot&lt;T&gt;）：逻辑执行链内自动捕获，支持嵌套；
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
