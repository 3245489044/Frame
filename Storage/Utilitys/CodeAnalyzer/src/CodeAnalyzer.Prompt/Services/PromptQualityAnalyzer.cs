using CodeAnalyzer.Analysis.Services;
using CodeAnalyzer.Core.Models;
using CodeAnalyzer.Prompt.Constants;

namespace CodeAnalyzer.Prompt.Services;

public static class PromptQualityAnalyzer
{
    public static PromptQualityContext Analyze(string code, AnalysisConfiguration? config = null)
    {
        if (string.IsNullOrWhiteSpace(code))
            return new PromptQualityContext(new AnalysisResult { Error = PromptRecommenderConstants.Defaults.ErrorCodeEmpty });

        config ??= new AnalysisConfiguration();
        try
        {
            var result = CodeAnalysisEngine.Analyze(code, config);
            if (string.IsNullOrEmpty(result.Error)) result.Error = null!;
            if (result.Error != null) return new PromptQualityContext(result);

            var suggestions = RefactoringSuggestionGenerator.GenerateSuggestions(result.Elements, result.Clusters, config);
            var assessment = OptimizationAssessmentCalculator.DetermineOptimizationAssessment(suggestions, result);
            result.Suggestions = suggestions;
            return new PromptQualityContext(result, assessment);
        }
        catch (Exception ex)
        {
            return new PromptQualityContext(new AnalysisResult { Error = string.Format(PromptRecommenderConstants.Messages.ErrorAnalysisEngineExceptionTemplate, ex.Message) });
        }
    }

    public static IDictionary<string, object> GetQuickMetrics(string code, AnalysisConfiguration? config = null)
    {
        var ctx = Analyze(code, config);
        return new Dictionary<string, object>
        {
            [PromptRecommenderConstants.AnalysisKeys.Assessment] = ctx.AssessmentDisplay,
            [PromptRecommenderConstants.AnalysisKeys.AssessmentDescription] = ctx.AssessmentDescription,
            [PromptRecommenderConstants.AnalysisKeys.TotalElements] = ctx.GetMetric<int>(PromptRecommenderConstants.AnalysisKeys.TotalElements, 0),
            [PromptRecommenderConstants.AnalysisKeys.TotalSuggestions] = ctx.TotalSuggestions,
            [PromptRecommenderConstants.AnalysisKeys.HighConfidenceSuggestions] = ctx.HighConfidenceSuggestions,
            [PromptRecommenderConstants.AnalysisKeys.AverageComplexity] = ctx.GetMetric<double>(PromptRecommenderConstants.AnalysisKeys.AverageComplexity, 0.0),
            [PromptRecommenderConstants.AnalysisKeys.AverageVariableReuseRatio] = ctx.GetMetric<double>(PromptRecommenderConstants.AnalysisKeys.AverageVariableReuseRatio, 0.0),
            [PromptRecommenderConstants.AnalysisKeys.HighComplexityMethods] = ctx.GetMetric<int>(PromptRecommenderConstants.AnalysisKeys.HighComplexityMethods, 0),
            [PromptRecommenderConstants.AnalysisKeys.LargeClassCount] = ctx.GetMetric<int>(PromptRecommenderConstants.AnalysisKeys.LargeClassCount, 0),
            [PromptRecommenderConstants.AnalysisKeys.AverageCohesion] = ctx.GetMetric<double>(PromptRecommenderConstants.AnalysisKeys.AverageCohesion, 0.0),
            [PromptRecommenderConstants.AnalysisKeys.AverageCoupling] = ctx.GetMetric<double>(PromptRecommenderConstants.AnalysisKeys.AverageCoupling, 0.0),
            [PromptRecommenderConstants.AnalysisKeys.DominantCategory] = ctx.GetDominantCategory().Category.ToString(),
            [PromptRecommenderConstants.AnalysisKeys.DominantScore] = ctx.GetDominantCategory().Score
        };
    }
}
