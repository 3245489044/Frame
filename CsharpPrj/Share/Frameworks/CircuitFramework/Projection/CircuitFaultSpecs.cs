namespace Share.CircuitFramework.Projection;

/// <summary>
/// 重试声明（G4 声明面）：承载节点失败 → 重试（最大尝试次数 + 指数退避基底毫秒）。
/// 投影为 <see cref="Symbols.RetrySymbol"/>；阈值/熔断归宿主（框架只做机制）。
/// </summary>
/// <param name="NodeId">承载重试策略的节点 Id（须已声明步骤；存在性由 CFG-024 判红）。</param>
/// <param name="MaxAttempts">最大尝试次数（含首次；&lt;= 1 = 不重试）。</param>
/// <param name="BackoffBaseMs">指数退避基底毫秒（退避 = base × 2^(n-1)）。</param>
public sealed record CircuitRetrySpec(string NodeId, int MaxAttempts, double BackoffBaseMs = 100);

/// <summary>失败路由声明（G4 声明面）：主路径用尽 → 备选节点（同入参，产物以主节点 Id 回填）。</summary>
/// <param name="PrimaryNodeId">主路径节点 Id。</param>
/// <param name="AlternativeNodeId">备选节点 Id（存在性由 CFG-024 判红）。</param>
public sealed record CircuitFallbackSpec(string PrimaryNodeId, string AlternativeNodeId);

/// <summary>受保护节点声明（G4 声明面）：失败 → 吞并继续（不产输出）。</summary>
/// <param name="ProtectedNodeIds">受保护作用域内的节点集合。</param>
public sealed record CircuitGuardSpec(IReadOnlyList<string> ProtectedNodeIds);
