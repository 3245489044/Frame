using CodeAnalyzer.Analysis.Services;
using CodeAnalyzer.Core.Models;
using CodeAnalyzer.Prompt.Constants;

namespace CodeAnalyzer.Prompt.Services;

public class PromptRecommender
{
    private readonly AnalysisConfiguration _config;

    public PromptRecommender(AnalysisConfiguration? config = null) => _config = config ?? new();

    public RecommendedPrompt RecommendForCode(string code)
    {
        var ctx = PromptQualityAnalyzer.Analyze(code, _config);
        return ctx.RawResult.Error != null ? CreateErrorPrompt(ctx.RawResult.Error) : RecommendBasedOnContext(ctx);
    }

    private RecommendedPrompt RecommendBasedOnContext(PromptQualityContext ctx)
    {
        if (!ctx.CategoryScores?.Any() ?? true) return CreateGeneralPrompt(ctx);
        var (cat, score) = ctx.GetDominantCategory();
        if (score < PromptRecommenderConstants.Thresholds.DominantScoreMedium) return CreateGeneralPrompt(ctx);

        var type = RecommendationAnalyzer.MapScoreToRecommendation((cat, score), ctx.CategoryScores.ToDictionary(k => k.Key, k => k.Value));
        double conf = RecommendationAnalyzer.CalculateConfidence(ctx.CategoryScores.ToDictionary(k => k.Key, k => k.Value), (cat, score));
        var support = ctx.CategoryScores.Where(kv => kv.Key != cat && kv.Value > PromptRecommenderConstants.Thresholds.MinSupportingScore)
                                        .ToDictionary(kv => kv.Key, kv => kv.Value);
        return BuildPromptFromRecommendation(new RecommendationResult { Type = type, Confidence = conf, DominantCategory = cat, DominantScore = score, SupportingScores = support }, ctx);
    }

    private RecommendedPrompt BuildPromptFromRecommendation(RecommendationResult rec, PromptQualityContext ctx)
    {
        if (rec.Confidence < PromptRecommenderConstants.Thresholds.ConfidenceLow) return CreateGeneralPrompt(ctx);
        if (!PromptRecommenderConstants.RecommendationMapping.TryGetValue(rec.Type, out var map))
            return new RecommendedPrompt { Prompt = PromptRecommenderConstants.Messages.GeneralOptimizationPrompt, Confidence = rec.Confidence, Reason = PromptRecommenderConstants.Messages.GeneralOptimizationReason, AnalysisDetails = BuildDetails(rec, ctx) };

        var val = ctx.GetMetric<object>(map.MetricKey);
        string reason = val switch
        {
            int i when i > 0 => string.Format(map.ReasonTemplate, i),
            double d when d > 0 => string.Format(map.ReasonTemplate, d),
            _ => map.FallbackReason
        };
        return new RecommendedPrompt
        {
            Prompt = map.PromptTemplate,
            Confidence = Math.Min(rec.Confidence, PromptRecommenderConstants.Thresholds.MaxConfidence),
            Reason = reason,
            AnalysisDetails = BuildDetails(rec, ctx)
        };
    }

    private Dictionary<string, object> BuildDetails(RecommendationResult rec, PromptQualityContext ctx) => new()
    {
        [PromptRecommenderConstants.AnalysisKeys.RecommendationType] = rec.Type.ToString(),
        [PromptRecommenderConstants.AnalysisKeys.DominantCategory] = rec.DominantCategory.ToString(),
        [PromptRecommenderConstants.AnalysisKeys.DominantScore] = rec.DominantScore,
        [PromptRecommenderConstants.AnalysisKeys.Confidence] = rec.Confidence,
        [PromptRecommenderConstants.AnalysisKeys.SupportingCategories] = rec.SupportingScores.Keys.Select(RecommendationAnalyzer.GetScoreCategoryName).ToList(),
        [PromptRecommenderConstants.AnalysisKeys.AnalysisMetrics] = ctx.Metrics,
        [PromptRecommenderConstants.AnalysisKeys.TotalSuggestions] = ctx.TotalSuggestions,
        [PromptRecommenderConstants.AnalysisKeys.HighConfidenceSuggestions] = ctx.HighConfidenceSuggestions,
        [PromptRecommenderConstants.AnalysisKeys.Assessment] = ctx.AssessmentDisplay
    };

    private RecommendedPrompt CreateGeneralPrompt(PromptQualityContext ctx) => new()
    {
        Prompt = PromptRecommenderConstants.Messages.GeneralOptimizationPrompt,
        Confidence = PromptRecommenderConstants.Defaults.ZeroConfidence,
        Reason = PromptRecommenderConstants.Messages.GeneralOptimizationReason,
        AnalysisDetails = new Dictionary<string, object>
        {
            [PromptRecommenderConstants.AnalysisKeys.RecommendationType] = RecommendationType.GeneralOptimization.ToString(),
            [PromptRecommenderConstants.AnalysisKeys.Confidence] = PromptRecommenderConstants.Defaults.ZeroConfidence,
            [PromptRecommenderConstants.AnalysisKeys.Message] = PromptRecommenderConstants.Defaults.LowConfidenceMessage,
            [PromptRecommenderConstants.AnalysisKeys.TotalSuggestions] = ctx.TotalSuggestions,
            [PromptRecommenderConstants.AnalysisKeys.Assessment] = ctx.AssessmentDisplay
        }
    };

    private RecommendedPrompt CreateErrorPrompt(string error) => new()
    {
        Prompt = string.IsNullOrWhiteSpace(error) ? PromptRecommenderConstants.Messages.ErrorUnknownPrompt
                 : error.Contains(PromptRecommenderConstants.Defaults.ErrorCodeEmpty) ? PromptRecommenderConstants.Messages.ErrorEmptyCodePrompt
                 : PromptRecommenderConstants.Messages.SystemErrorPrompt,
        Confidence = PromptRecommenderConstants.Defaults.ZeroConfidence,
        Reason = string.IsNullOrWhiteSpace(error) ? PromptRecommenderConstants.Messages.ErrorUnknownReason
                 : error.Contains(PromptRecommenderConstants.Defaults.ErrorCodeEmpty) ? error
                 : $"{PromptRecommenderConstants.Defaults.ErrorAnalysisPrefix}{error}",
        AnalysisDetails = new Dictionary<string, object> { [PromptRecommenderConstants.AnalysisKeys.Error] = error ?? "", [PromptRecommenderConstants.AnalysisKeys.Confidence] = PromptRecommenderConstants.Defaults.ZeroConfidence }
    };
}
