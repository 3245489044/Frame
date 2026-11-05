using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace Modules.Concurrency;

/// <summary>声明式复杂度评估（零 LLM）：特征表来自 spec 快照，权重/饱和点/顺序全部声明驱动。</summary>
public sealed class DeclarativeComplexityAssessor : IComplexityAssessor
{
    private readonly ConcurrencyOptions _options;

    public DeclarativeComplexityAssessor(ConcurrencyOptions options)
        => _options = options ?? throw new ArgumentNullException(nameof(options));

    public Task<ComplexityAssessment> AssessAsync(ComplexityInput input, CancellationToken ct = default)
    {
        ArgumentNullException.ThrowIfNull(input);
        var options = _options.TaskComplexity;

        var features = new Dictionary<string, double>(StringComparer.Ordinal);
        var score = 0d;
        var totalWeight = 0d;
        foreach (var spec in ConcurrencyOptionSpecs.Features)
        {
            if (spec.SaturateAt <= 0)
                throw new InvalidOperationException($"特征 '{spec.Key}' 的 saturateAt 必须 > 0（声明面不可判定）。");
            var raw = ComplexityFeatureSelectors.Resolve(spec.Selector)(input);
            var normalized = Math.Clamp(raw / spec.SaturateAt, 0, 1);
            features[spec.Key] = normalized;
            score += normalized * spec.Weight;
            totalWeight += spec.Weight;
        }

        // 权重和 = 0 ⇒ 归一化不可判定（禁静默归零冒充「简单」）。
        if (totalWeight <= 0)
            throw new InvalidOperationException("复杂度特征权重和为 0：请修正 concurrency-options.spec.json 的 features 权重。");

        score = Math.Clamp(score / totalWeight, 0, 1);

        var band = score >= options.ThetaSplitHigh ? ComplexityBand.Complex
                 : score < options.ThetaSplitLow ? ComplexityBand.Simple
                 : ComplexityBand.Moderate;

        return Task.FromResult(new ComplexityAssessment(score, band, features, "declarative"));
    }
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
        var budget = _options.Budget;
        var split = ConcurrencyOptionSpecs.Split;
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

        // 分隔符/段数下限/成员上限一律读声明（禁代码字面量）。
        var segments = text.Split(split.Separators.ToArray(), StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Where(static s => !string.IsNullOrWhiteSpace(s))
            .Take(split.MaxMembers)
            .ToArray();
        if (segments.Length < split.MinSegments)
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
        var overhead = (long)Math.Ceiling(members.Length * (double)options.UnitTokensPerNode * budget.CoordOverheadRatio);
        var singleCost = (long)Math.Ceiling((totalTokens + overhead) * budget.SinglePenaltyFactor);
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
