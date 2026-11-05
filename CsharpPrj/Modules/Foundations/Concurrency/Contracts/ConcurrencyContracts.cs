using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Concurrency;

/// <summary>复杂度档：决定“拆不拆”，不决定“怎么拆”。</summary>
public enum ComplexityBand
{
    Simple = 0,
    Moderate = 1,
    Complex = 2,
}

/// <summary>复杂度评估结果：分数 + 档 + 可复算特征（禁黑箱分）。</summary>
public sealed record ComplexityAssessment(
    double Score,
    ComplexityBand Band,
    IReadOnlyDictionary<string, double> Features,
    string Source,
    string? Reason = null);

/// <summary>复杂度输入（结构事实；token 估算口径 = 字符数 × 声明系数）。</summary>
public sealed record ComplexityInput(
    string Text,
    int DeliverableCount = 0,
    int TargetKindCount = 0,
    int NodeCount = 0,
    int CrossModuleCount = 0,
    int SubAgentCount = 0,
    long EstimatedTokens = 0);

/// <summary>拆分计划成员（有界；组内 dependsOn 必须为空）。
/// <para><paramref name="Provider"/>/<paramref name="Model"/> 可选：成员级计价覆盖（null = 用声明面缺省）。</para></summary>
public sealed record SplitMember(
    string Goal,
    string? SubAgent,
    IReadOnlyList<string>? NodeRefs,
    long EstTokens,
    IReadOnlyList<string>? DependsOn,
    string? Provider = null,
    string? Model = null);

/// <summary>拆分计划（节点事实；可复用）。SingleEstimateTokens 供成本门禁比较“并发 vs 单次”。</summary>
public sealed record SplitPlan(
    string PlanKey,
    IReadOnlyList<SplitMember> Members,
    string InputDigest,
    long? SingleEstimateTokens = null,
    string? Note = null);

/// <summary>复杂度评估端口（默认零 LLM）。</summary>
public interface IComplexityAssessor
{
    Task<ComplexityAssessment> AssessAsync(ComplexityInput input, CancellationToken ct = default);
}

/// <summary>拆分规划端口（默认恒等拆分 = 1 成员）。</summary>
public interface ISplitPlanner
{
    Task<SplitPlan> PlanAsync(ComplexityAssessment assessment, ComplexityInput input, CancellationToken ct = default);
}

/// <summary>成本估算（token 口径；AmountMicros=null 表示未接价格源 = Unknown）。</summary>
public sealed record CostEstimate(
    long? AmountMicros,
    long EstTokens,
    string Currency,
    string Source);

/// <summary>并发准入回执：Denied 必带可复算比较结果与受控码。</summary>
public sealed record ConcurrencyAdmission(
    bool Admitted,
    string Code,
    CostEstimate GroupEstimate,
    CostEstimate SingleEstimate,
    int RequestedConcurrency,
    int AvailableSlots,
    string? Detail = null);

/// <summary>拆分计划存储端口（节点事实：落 ur_nodes，可 diff/可复用；宿主适配实现）。</summary>
public interface ISplitPlanStore
{
    Task<bool> SaveAsync(SplitPlan plan, CancellationToken ct = default);
    Task<SplitPlan?> TryGetAsync(string planKey, CancellationToken ct = default);
}

/// <summary>并发成本门禁（纯计算 + 只读策略；不改变状态）。</summary>
public interface IConcurrencyBudgetGate
{
    Task<ConcurrencyAdmission> EvaluateAsync(
        IReadOnlyList<SplitMember> members, long? singleEstimateTokens, CancellationToken ct = default);
}

/// <summary>组键派生策略（单源）：成员键/组键不得由端口手拼。</summary>
public interface IGroupKeyPolicy
{
    /// <summary>派生组键：scope + plan 键 + 序号（序号由调用方在重试/重复分发时递增）。</summary>
    string For(string scopeKey, SplitPlan plan, int attempt = 0);
}

/// <summary>组结果交付语义（通用，无领域词）。</summary>
public enum ConcurrencyWaitMode
{
    Await = 0,
    Detach = 1,
}
