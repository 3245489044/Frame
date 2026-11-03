using System;
using System.Collections.Generic;

namespace Modules.Concurrency;

/// <summary>节点复用统计（中性形态；宿主由租约审计映射）。</summary>
public readonly record struct ConcurrencyReuseStat(int Executed, int Waited, int Reused);

/// <summary>组员结算事实（中性形态；宿主由组结果映射）。</summary>
public sealed record ConcurrencyMemberSettlement(
    string TaskKey,
    long EstimatedTokens,
    long? ActualTokens,
    double DurationMs);

/// <summary>组结算事实（中性形态；宿主由 TaskGroupResult + SplitPlan 映射）。</summary>
public sealed record ConcurrencyGroupSettlement(
    string GroupKey,
    string? PlanKey,
    DateTimeOffset StartedAt,
    IReadOnlyList<ConcurrencyMemberSettlement> Members,
    ConcurrencyReuseStat Reuse);

/// <summary>成本跨度（中性形态；宿主映射到自有账本记录，不新增表/账本）。</summary>
public sealed record ConcurrencyCostSpan(
    string Schema,
    string IdempotencyKey,
    string GroupKey,
    string? PlanKey,
    string TaskKey,
    string StartedAt,
    long DurationMs,
    long? InputTokens,
    long? OutputTokens,
    long? TotalTokens,
    string UsageSource,
    string Currency,
    IReadOnlyDictionary<string, string>? Attributes);
