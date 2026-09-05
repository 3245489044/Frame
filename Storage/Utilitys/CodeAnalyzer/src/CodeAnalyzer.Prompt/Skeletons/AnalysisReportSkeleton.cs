using CodeAnalyzer.Core.Models;

namespace CodeAnalyzer.Prompt.Skeletons;

public static class AnalysisReportSkeleton
{
    public static string GenerateReport(AnalysisResult result, List<RefactoringSuggestion> suggestions, OptimizationAssessment assessment)
    {
        return Analysis.Services.AnalysisReportGenerator.GenerateReport(result, suggestions, assessment);
    }

    public static string GenerateEnhancedReport(AnalysisResult result, List<RefactoringSuggestion> suggestions, OptimizationAssessment assessment, RecommendedPrompt prompt)
    {
        var report = GenerateReport(result, suggestions, assessment);
        return Analysis.Services.EnhancedOptimizationAnalyzer.AnalyzeWithRecommendation(
            result.Elements.FirstOrDefault()?.Name ?? "", new AnalysisConfiguration()).Report;
    }
}
