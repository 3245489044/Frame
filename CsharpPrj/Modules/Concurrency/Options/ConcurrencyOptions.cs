using System;

namespace Modules.Concurrency;

/// <summary>复杂度声明的强类型视图（不可变快照；宿主绑定后只读）。</summary>
public sealed record TaskComplexityOptions
{
    public double ThetaSplitLow { get; init; } = 0.35;
    public double ThetaSplitHigh { get; init; } = 0.65;
    public bool SemanticLegEnabled { get; init; }
    public double CalibrationTolerance { get; init; } = 0.20;
    public double WeightInputTokens { get; init; } = 0.25;
    public double WeightDeliverables { get; init; } = 0.25;
    public double WeightTargetKinds { get; init; } = 0.15;
    public double WeightNodeCount { get; init; } = 0.20;
    public double WeightCrossModule { get; init; } = 0.10;
    public double WeightSubAgents { get; init; } = 0.05;
    public double TokensPerChar { get; init; } = 0.6;
    public long UnitTokensPerNode { get; init; } = 256;

    /// <summary>不可变类型 ⇒ 复制即自身（保留快照语义，调用方零改动）。</summary>
    public TaskComplexityOptions Copy() => this;
}

/// <summary>并发成本门禁声明（不可变快照；宿主绑定后只读）。</summary>
public sealed record ConcurrencyBudgetOptions
{
    /// <summary>协调开销系数（禁 0；O_coord = N × unit × ratio）。</summary>
    public double CoordOverheadRatio { get; init; } = 0.05;
    /// <summary>无历史节点复用率；出厂 0 = 保守。</summary>
    public double ReuseProbDefault { get; init; }
    /// <summary>缺省 false = 估不出成本就不并发。</summary>
    public bool AllowUnknownCost { get; init; }
    /// <summary>组预算（token；0 = 未设限，仅做相对比较）。</summary>
    public long BudgetTokens { get; init; }
    /// <summary>可用并发名额上限（同时受宿主全局闸约束）。</summary>
    public int MaxConcurrentCalls { get; init; } = 4;
    /// <summary>单次估算缺省放大系数（单任务把全量塞进一次调用的惩罚；≥1）。</summary>
    public double SinglePenaltyFactor { get; init; } = 1.0;
    /// <summary>每节点复用节省的 token 单位。</summary>
    public long UnitTokensPerNode { get; init; } = 256;
    /// <summary>输出 token 占估算 token 的比例（金额口径分离 input/output；0..1）。</summary>
    public double OutputTokenRatio { get; init; } = 0.5;
    /// <summary>缓存读 token 占估算 token 的比例（金额口径分离 cache；0..1）。</summary>
    public double CacheReadRatio { get; init; }
    /// <summary>计价模型提供方（null ⇒ 金额口径 Unknown）。</summary>
    public string? Provider { get; init; }
    /// <summary>计价模型（null ⇒ 金额口径 Unknown）。</summary>
    public string? Model { get; init; }

    /// <summary>不可变类型 ⇒ 复制即自身（保留快照语义，调用方零改动）。</summary>
    public ConcurrencyBudgetOptions Copy() => this;
}

/// <summary>
/// 并发策略不可变快照：宿主在组合根一次性生成，内核只读本快照，禁读进程静态全局。
/// <para>单一构造入口 = <see cref="From"/>（禁共享可写实例；无 <c>Default</c> 逃逸面）。</para>
/// </summary>
public sealed record ConcurrencyOptions(
    TaskComplexityOptions TaskComplexity,
    ConcurrencyBudgetOptions Budget)
{
    public static ConcurrencyOptions From(TaskComplexityOptions? complexity, ConcurrencyBudgetOptions? budget)
        => new(
            (complexity ?? new TaskComplexityOptions()).Copy(),
            (budget ?? new ConcurrencyBudgetOptions()).Copy());
}

