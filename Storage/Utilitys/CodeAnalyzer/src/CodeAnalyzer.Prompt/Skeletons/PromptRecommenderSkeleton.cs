using CodeAnalyzer.Core.Models;

namespace CodeAnalyzer.Prompt.Skeletons;

public static class PromptRecommenderSkeleton
{
    public static RecommendedPrompt RecommendForCode(string code, AnalysisConfiguration? config = null)
    {
        config ??= new AnalysisConfiguration();
        var recommender = new Services.PromptRecommender(config);
        return recommender.RecommendForCode(code);
    }

    public static IDictionary<string, object> GetQuickMetrics(string code, AnalysisConfiguration? config = null)
    {
        return Services.PromptQualityAnalyzer.GetQuickMetrics(code, config);
    }
}
