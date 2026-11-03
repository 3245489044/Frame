using System;

namespace Modules.Concurrency;

/// <summary>复杂度声明的强类型视图（L3 可变；宿主负责绑定）。</summary>
public sealed class TaskComplexityOptions
{
    public double ThetaSplitLow { get; set; } = 0.35;
    public double ThetaSplitHigh { get; set; } = 0.65;
    public bool SemanticLegEnabled { get; set; }
    public double CalibrationTolerance { get; set; } = 0.20;
    public double WeightInputTokens { get; set; } = 0.25;
    public double WeightDeliverables { get; set; } = 0.25;
    public double WeightTargetKinds { get; set; } = 0.15;
    public double WeightNodeCount { get; set; } = 0.20;
    public double WeightCrossModule { get; set; } = 0.10;
    public double WeightSubAgents { get; set; } = 0.05;
    public double TokensPerChar { get; set; } = 0.6;
    public long UnitTokensPerNode { get; set; } = 256;

    public TaskComplexityOptions Clone() => (TaskComplexityOptions)MemberwiseClone();
}

/// <summary>并发成本门禁声明（L3 可变；宿主负责绑定）。</summary>
public sealed class ConcurrencyBudgetOptions
{
    /// <summary>协调开销系数（禁 0；O_coord = N × unit × ratio）。</summary>
    public double CoordOverheadRatio { get; set; } = 0.05;
    /// <summary>无历史节点复用率；出厂 0 = 保守。</summary>
    public double ReuseProbDefault { get; set; }
    /// <summary>缺省 false = 估不出成本就不并发。</summary>
    public bool AllowUnknownCost { get; set; }
    /// <summary>组预算（token；0 = 未设限，仅做相对比较）。</summary>
    public long BudgetTokens { get; set; }
    /// <summary>可用并发名额上限（同时受宿主全局闸约束）。</summary>
    public int MaxConcurrentCalls { get; set; } = 4;
    /// <summary>单次估算缺省放大系数（单任务把全量塞进一次调用的惩罚；≥1）。</summary>
    public double SinglePenaltyFactor { get; set; } = 1.0;
    /// <summary>每节点复用节省的 token 单位。</summary>
    public long UnitTokensPerNode { get; set; } = 256;
    /// <summary>输出 token 占估算 token 的比例（金额口径分离 input/output；0..1）。</summary>
    public double OutputTokenRatio { get; set; } = 0.5;
    /// <summary>缓存读 token 占估算 token 的比例（金额口径分离 cache；0..1）。</summary>
    public double CacheReadRatio { get; set; }
    /// <summary>计价模型提供方（null ⇒ 金额口径 Unknown）。</summary>
    public string? Provider { get; set; }
    /// <summary>计价模型（null ⇒ 金额口径 Unknown）。</summary>
    public string? Model { get; set; }

    public ConcurrencyBudgetOptions Clone() => (ConcurrencyBudgetOptions)MemberwiseClone();
}

/// <summary>
/// 并发策略不可变快照：宿主在组合根一次性生成，内核只读本快照，禁读进程静态全局。
/// </summary>
public sealed record ConcurrencyOptions(
    TaskComplexityOptions TaskComplexity,
    ConcurrencyBudgetOptions Budget)
{
    public static ConcurrencyOptions Default { get; } = new(new TaskComplexityOptions(), new ConcurrencyBudgetOptions());

    public static ConcurrencyOptions From(TaskComplexityOptions? complexity, ConcurrencyBudgetOptions? budget)
        => new(
            (complexity ?? new TaskComplexityOptions()).Clone(),
            (budget ?? new ConcurrencyBudgetOptions()).Clone());
}
