using CodeAnalyzer.Core.Models;

namespace CodeAnalyzer.Analysis.Services;

public static class OptimizationAssessmentCalculator
{
    public static OptimizationAssessment DetermineOptimizationAssessment(List<RefactoringSuggestion> suggestions, AnalysisResult result)
    {
        if (!suggestions.Any()) return OptimizationAssessment.NotRequired;
        var score = CalculateAssessmentScore(suggestions, result) * CalculateScaleFactor(result.Elements.Count);
        return MapScoreToAssessment(score);
    }

    private static double CalculateAssessmentScore(List<RefactoringSuggestion> suggestions, AnalysisResult result)
    {
        double score = suggestions.Average(s => s.Confidence) * 0.6;
        if (result.Elements.Count > 0)
        {
            double suggestionRatio = (double)suggestions.Count / result.Elements.Count;
            score += Math.Log(1 + suggestionRatio) * 0.15;
        }
        score += CalculateTypeWeightScore(suggestions) * 0.25;
        int highConfidenceCount = suggestions.Count(s => s.Confidence > CodeAnalysisConstants.CONFIDENCE_HIGH);
        score += highConfidenceCount * 0.02;
        if (result.Elements.Count < 10) score *= 0.85;
        return Math.Max(score, 0);
    }

    private static double CalculateTypeWeightScore(List<RefactoringSuggestion> suggestions)
    {
        double score = 0.0;
        foreach (var suggestion in suggestions)
        {
            double typeWeight = suggestion.OptimizationType switch
            {
                RefactoringTypeNames.METHOD_OPTIMIZATION => 0.9,
                RefactoringTypeNames.CLASS_EXTRACTION => 1.0,
                RefactoringTypeNames.VARIABLE_OPTIMIZATION => 0.5,
                RefactoringTypeNames.PARAMETER_OBJECT_EXTRACTION => 0.7,
                _ => 0.6
            };
            score += suggestion.Confidence * typeWeight;
        }
        return score / Math.Max(suggestions.Count, 1);
    }

    private static double CalculateScaleFactor(int elementCount)
    {
        return elementCount switch
        {
            <= 5 => CodeAnalysisConstants.ASSESSMENT_SCALE_LOW,
            <= 10 => CodeAnalysisConstants.ASSESSMENT_SCALE_MEDIUM,
            <= 20 => CodeAnalysisConstants.ASSESSMENT_SCALE_HIGH,
            <= 50 => CodeAnalysisConstants.ASSESSMENT_SCALE_VERY_HIGH,
            _ => CodeAnalysisConstants.ASSESSMENT_SCALE_EXTREME
        };
    }

    private static OptimizationAssessment MapScoreToAssessment(double score)
    {
        return score switch
        {
            < CodeAnalysisConstants.ASSESSMENT_NOT_REQUIRED_THRESHOLD => OptimizationAssessment.NotRequired,
            < CodeAnalysisConstants.ASSESSMENT_LOW_THRESHOLD => OptimizationAssessment.LowPriority,
            < CodeAnalysisConstants.ASSESSMENT_MEDIUM_THRESHOLD => OptimizationAssessment.MediumPriority,
            < CodeAnalysisConstants.ASSESSMENT_HIGH_THRESHOLD => OptimizationAssessment.HighPriority,
            _ => OptimizationAssessment.CriticalPriority
        };
    }

    public static string GetAssessmentDisplay(OptimizationAssessment assessment) =>
        assessment == OptimizationAssessment.NotRequired ? "不需要优化" :
        assessment == OptimizationAssessment.LowPriority ? "低优先级优化" :
        assessment == OptimizationAssessment.MediumPriority ? "中等优先级优化" :
        assessment == OptimizationAssessment.HighPriority ? "高优先级优化" : "关键优先级优化";

    public static string GetAssessmentDescription(OptimizationAssessment assessment) =>
        assessment == OptimizationAssessment.NotRequired ? "代码质量良好，无需优化" :
        assessment == OptimizationAssessment.LowPriority ? "存在少量可优化点，建议在下次重构时处理" :
        assessment == OptimizationAssessment.MediumPriority ? "存在中等程度的优化空间，建议安排时间进行重构" :
        assessment == OptimizationAssessment.HighPriority ? "存在较多优化机会，建议尽快进行代码重构" : "代码质量急需优化，建议立即进行重构";
}
