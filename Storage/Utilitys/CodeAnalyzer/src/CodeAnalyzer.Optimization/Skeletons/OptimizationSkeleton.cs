using CodeAnalyzer.Analysis.Services;
using CodeAnalyzer.Core.Models;
using CodeAnalyzer.Prompt.Services;

namespace CodeAnalyzer.Optimization.Skeletons;

public static class OptimizationSkeleton
{
    public static (OptimizationAssessment Assessment, string Report, RecommendedPrompt Prompt) AnalyzeWithRecommendation(
        string code, AnalysisConfiguration? config = null)
    {
        config ??= new AnalysisConfiguration();
        return EnhancedOptimizationAnalyzer.AnalyzeWithRecommendation(code, config);
    }

    public static OptimizationAssessment QuickAssess(string code, AnalysisConfiguration? config = null)
    {
        var (assessment, _, _) = AnalyzeWithRecommendation(code, config);
        return assessment;
    }

    public static IDictionary<string, object> GetQuickMetrics(string code, AnalysisConfiguration? config = null)
    {
        return PromptQualityAnalyzer.GetQuickMetrics(code, config);
    }
}
