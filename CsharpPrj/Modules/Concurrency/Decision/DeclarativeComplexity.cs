using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Concurrency;

/// <summary>声明式复杂度评估（零 LLM）：结构特征加权，权重/阈值全部来自声明快照。</summary>
public sealed class DeclarativeComplexityAssessor : IComplexityAssessor
{
    private readonly ConcurrencyOptions _options;

    public DeclarativeComplexityAssessor(ConcurrencyOptions options)
        => _options = options ?? throw new ArgumentNullException(nameof(options));

    public Task<ComplexityAssessment> AssessAsync(ComplexityInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var options = _options.TaskComplexity;

        var features = new Dictionary<string, double>(StringComparer.Ordinal)
        {
            ["input_tokens"] = Normalize(input.Text?.Length ?? 0, 4000),
            ["deliverables"] = Normalize(input.DeliverableCount, 8),
            ["target_kinds"] = Normalize(input.TargetKindCount, 5),
            ["node_count"] = Normalize(input.NodeCount, 16),
            ["cross_module"] = Normalize(input.CrossModuleCount, 4),
            ["sub_agents"] = Normalize(input.SubAgentCount, 4),
        };

        var score = features["input_tokens"] * options.WeightInputTokens
                  + features["deliverables"] * options.WeightDeliverables
                  + features["target_kinds"] * options.WeightTargetKinds
                  + features["node_count"] * options.WeightNodeCount
                  + features["cross_module"] * options.WeightCrossModule
                  + features["sub_agents"] * options.WeightSubAgents;

        var totalWeight = options.WeightInputTokens + options.WeightDeliverables + options.WeightTargetKinds
                        + options.WeightNodeCount + options.WeightCrossModule + options.WeightSubAgents;
        if (totalWeight > 0) score /= totalWeight;
        score = Math.Clamp(score, 0, 1);

        var band = score >= options.ThetaSplitHigh ? ComplexityBand.Complex
                 : score < options.ThetaSplitLow ? ComplexityBand.Simple
                 : ComplexityBand.Moderate;

        return Task.FromResult(new ComplexityAssessment(score, band, features, "declarative"));
    }

    private static double Normalize(int value, int saturateAt)
        => saturateAt <= 0 ? 0 : Math.Clamp(value / (double)saturateAt, 0, 1);
}

/// <summary>声明式拆分：默认恒等（1 成员 = 不拆）；Complex 才按行/分号做有界拆分。</summary>
public sealed class DeclarativeSplitPlanner : ISplitPlanner
{
    private readonly ConcurrencyOptions _options;

    public DeclarativeSplitPlanner(ConcurrencyOptions options)
        => _options = options ?? throw new ArgumentNullException(nameof(options));

    public Task<SplitPlan> PlanAsync(ComplexityAssessment assessment, ComplexityInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(assessment);
        ArgumentNullException.ThrowIfNull(input);

        var options = _options.TaskComplexity;
        var text = input.Text ?? string.Empty;
        var inputDigest = Digest(text);
        var totalTokens = input.EstimatedTokens > 0
            ? input.EstimatedTokens
            : (long)Math.Ceiling(text.Length * options.TokensPerChar + input.NodeCount * options.UnitTokensPerNode);

        if (assessment.Band != ComplexityBand.Complex)
        {
            var identity = new SplitMember(string.IsNullOrWhiteSpace(text) ? "task" : text, null,
                Array.Empty<string>(), totalTokens, Array.Empty<string>());
            return Task.FromResult(new SplitPlan(
                "plan:" + inputDigest,
                new[] { identity },
                inputDigest,
                SingleEstimateTokens: totalTokens,
                Note: "identity"));
        }

        var segments = text.Split(new[] { '\n', ';', '；' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static s => !string.IsNullOrWhiteSpace(s))
            .Take(8)
            .ToArray();
        if (segments.Length <= 1)
        {
            var identity = new SplitMember(string.IsNullOrWhiteSpace(text) ? "task" : text, null,
                Array.Empty<string>(), totalTokens, Array.Empty<string>());
            return Task.FromResult(new SplitPlan("plan:" + inputDigest, new[] { identity }, inputDigest,
                SingleEstimateTokens: totalTokens, Note: "complex-but-single"));
        }

        var per = Math.Max(1, totalTokens / segments.Length);
        var members = segments.Select(s => new SplitMember(s, null, Array.Empty<string>(), per, Array.Empty<string>()))
            .ToArray();
        // 单次成本必须含「协调开销 + 单任务放大」，否则组成本恒 ≥ 单次成本 ⇒ 生产路径永远判不划算。
        var budget = _options.Budget;
        var overhead = (long)Math.Ceiling(
            members.Length * Math.Max(1, options.UnitTokensPerNode) * Math.Max(0, budget.CoordOverheadRatio));
        var singleCost = (long)Math.Ceiling((totalTokens + overhead) * Math.Max(1, budget.SinglePenaltyFactor));
        return Task.FromResult(new SplitPlan(
            "plan:" + inputDigest,
            members,
            inputDigest,
            SingleEstimateTokens: singleCost,
            Note: "declarative-lines"));
    }

    private static string Digest(string text)
    {
        var hash = SHA256.HashData(Encoding.UTF8.GetBytes(text ?? string.Empty));
        return Convert.ToHexString(hash)[..16];
    }
}
