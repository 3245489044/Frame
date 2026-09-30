using Share.CircuitFramework.Core;

namespace Share.CircuitFramework.Symbols;

/// <summary>TryGuard：把一组节点包进可失败作用域。</summary>
public sealed class TryGuardSymbol : CircuitSymbol
{
    public override CircuitSymbolKind Kind => CircuitSymbolKind.TryGuard;
    public override CircuitSymbolCategory Category => CircuitSymbolCategory.FaultTolerance;

    /// <summary>受保护作用域内的节点集合。</summary>
    public required IReadOnlyList<string> ProtectedNodeIds { get; init; }
}

/// <summary>CatchRoute：失败后路由到补救子电路。</summary>
public sealed class CatchRouteSymbol : CircuitSymbol
{
    public override CircuitSymbolKind Kind => CircuitSymbolKind.CatchRoute;
    public override CircuitSymbolCategory Category => CircuitSymbolCategory.FaultTolerance;

    /// <summary>承载该路由的节点（异常来源）。</summary>
    public required string NodeId { get; init; }

    /// <summary>补救子电路/节点。</summary>
    public required string RemedyNodeId { get; init; }
}

/// <summary>Retry：可配置重试（次数/退避）。</summary>
public sealed class RetrySymbol : CircuitSymbol
{
    public override CircuitSymbolKind Kind => CircuitSymbolKind.Retry;
    public override CircuitSymbolCategory Category => CircuitSymbolCategory.FaultTolerance;

    /// <summary>承载该重试策略的节点。</summary>
    public required string NodeId { get; init; }

    /// <summary>最大重试次数。</summary>
    public required int MaxAttempts { get; init; }

    /// <summary>指数退避基底的毫秒数。</summary>
    public double BackoffBaseMs { get; init; } = 100;
}

/// <summary>Fallback：主路径失败 → 备选电路。</summary>
public sealed class FallbackSymbol : CircuitSymbol
{
    public override CircuitSymbolKind Kind => CircuitSymbolKind.Fallback;
    public override CircuitSymbolCategory Category => CircuitSymbolCategory.FaultTolerance;

    /// <summary>主路径节点。</summary>
    public required string PrimaryNodeId { get; init; }

    /// <summary>备选电路/节点。</summary>
    public required string AlternativeNodeId { get; init; }
}