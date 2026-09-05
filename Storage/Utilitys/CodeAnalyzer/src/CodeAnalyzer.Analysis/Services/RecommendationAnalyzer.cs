using CodeAnalyzer.Core.Models;

namespace CodeAnalyzer.Analysis.Services;

public static class RecommendationAnalyzer
{
    public static Dictionary<ScoreCategory, double> CalculateComprehensiveScores(AnalysisResult result)
    {
        return new Dictionary<ScoreCategory, double>
        {
            [ScoreCategory.Architecture] = CalculateArchitectureScore(result),
            [ScoreCategory.Method] = CalculateMethodScore(result),
            [ScoreCategory.Variable] = CalculateVariableScore(result),
            [ScoreCategory.Stability] = CalculateStabilityScore(result),
            [ScoreCategory.Complexity] = CalculateComplexityScore(result),
            [ScoreCategory.General] = CalculateGeneralScore(result)
        };
    }

    private static double CalculateArchitectureScore(AnalysisResult result)
    {
        if (result.Clusters.Count == 0) return 0.0;
        double score = 0.0;
        double avgCohesion = result.Clusters.Average(c => c.Cohesion);
        double avgCoupling = result.Clusters.Average(c => c.Coupling);
        score += (CodeAnalysisConstants.CONFIDENCE_MAX - avgCohesion) * CodeAnalysisConstants.ARCHITECTURE_SCORE_WEIGHT;
        score += avgCoupling * CodeAnalysisConstants.ARCHITECTURE_SCORE_WEIGHT;
        int classRefactoringCount = result.Suggestions.Count(s => s.Type == RefactoringType.ExtractClass || s.Type == RefactoringType.MergeClasses);
        score += Math.Min(classRefactoringCount * 0.2, 0.5);
        return Math.Clamp(score, 0, CodeAnalysisConstants.CONFIDENCE_MAX);
    }

    private static double CalculateMethodScore(AnalysisResult result)
    {
        var methods = result.Elements.Where(e => e.Type == ElementType.Method).ToList();
        if (!methods.Any()) return 0.0;
        double score = 0.0;
        int highComplexityCount = methods.Count(m => m.ComplexityScore > CodeAnalysisConstants.HIGH_COMPLEXITY_THRESHOLD);
        score += (double)highComplexityCount / methods.Count * 0.3;
        int longMethodCount = methods.Count(m => m.BodyLines > CodeAnalysisConstants.LONG_METHOD_LINES_THRESHOLD);
        score += (double)longMethodCount / methods.Count * 0.3;
        int manyParamCount = methods.Count(m => m.Parameters.Count > CodeAnalysisConstants.MANY_PARAMETERS_THRESHOLD);
        score += (double)manyParamCount / methods.Count * 0.2;
        int methodRefactoringCount = result.Suggestions.Count(s => s.Type == RefactoringType.ExtractMethod || s.Type == RefactoringType.InlineMethod);
        score += Math.Min(methodRefactoringCount * CodeAnalysisConstants.METHOD_REFACTORING_WEIGHT, 0.2);
        return Math.Clamp(score, 0, CodeAnalysisConstants.CONFIDENCE_MAX);
    }

    private static double CalculateVariableScore(AnalysisResult result)
    {
        var elementsWithVars = result.Elements.Where(e => e.VariableCount > 0).ToList();
        if (!elementsWithVars.Any()) return 0.0;
        double score = 0.0;
        if (result.Metrics.TryGetValue("EffectiveVariableReuseRatio", out var reuseObj) && reuseObj is double reuseRatio)
            score += (CodeAnalysisConstants.CONFIDENCE_MAX - Math.Min(reuseRatio, CodeAnalysisConstants.CONFIDENCE_MAX)) * CodeAnalysisConstants.VARIABLE_REUSE_WEIGHT;
        int totalVariables = result.Elements.Sum(e => e.VariableCount);
        int unusedVariables = result.Elements.Sum(e => e.Variables.Count(v => v.UsageCount <= 1));
        if (totalVariables > 0) score += (double)unusedVariables / totalVariables * 0.3;
        int variableOptimizationCount = result.Suggestions.Count(s => s.OptimizationType == RefactoringTypeNames.VARIABLE_OPTIMIZATION);
        score += Math.Min(variableOptimizationCount * 0.15, 0.2);
        return Math.Clamp(score, 0, CodeAnalysisConstants.CONFIDENCE_MAX);
    }

    private static double CalculateStabilityScore(AnalysisResult result)
    {
        double score = 0.0;
        var methods = result.Elements.Where(e => e.Type == ElementType.Method).ToList();
        if (methods.Any())
        {
            int potentialIssues = 0;
            foreach (var method in methods)
            {
                if (method.ComplexityScore > CodeAnalysisConstants.HIGH_COMPLEXITY_THRESHOLD) potentialIssues++;
                if (method.Parameters.Count > CodeAnalysisConstants.TOO_MANY_PARAMETERS_THRESHOLD) potentialIssues++;
                if (method.VariableCount > 10) potentialIssues++;
            }
            score += Math.Min((double)potentialIssues / methods.Count * 0.5, 0.5);
        }
        int bugFixSuggestions = result.Suggestions.Count(s => s.Risks.Any(r => r.Contains("异常") || r.Contains("错误") || r.Contains("崩溃")));
        score += Math.Min(bugFixSuggestions * 0.25, 0.5);
        return Math.Clamp(score, 0, CodeAnalysisConstants.CONFIDENCE_MAX);
    }

    private static double CalculateComplexityScore(AnalysisResult result)
    {
        var methods = result.Elements.Where(e => e.Type == ElementType.Method).ToList();
        if (!methods.Any()) return 0.0;
        double score = 0.0;
        double avgComplexity = methods.Average(m => m.ComplexityScore);
        score += Math.Min(avgComplexity / CodeAnalysisConstants.VERY_HIGH_COMPLEXITY_THRESHOLD, CodeAnalysisConstants.COMPLEXITY_SCORE_WEIGHT);
        double avgBodyLines = methods.Average(m => m.BodyLines);
        score += Math.Min(avgBodyLines / CodeAnalysisConstants.VERY_LONG_METHOD_LINES_THRESHOLD, 0.3);
        int highCyclomaticCount = methods.Count(m => m.ComplexityScore > CodeAnalysisConstants.HIGH_COMPLEXITY_THRESHOLD);
        score += (double)highCyclomaticCount / methods.Count * 0.3;
        return Math.Clamp(score, 0, CodeAnalysisConstants.CONFIDENCE_MAX);
    }

    private static double CalculateGeneralScore(AnalysisResult result)
    {
        double score = 0.0;
        if (result.Metrics.TryGetValue("TotalElements", out var totalObj) && totalObj is int total)
            score += Math.Min(total * 0.01, 0.2);
        if (result.Metrics.TryGetValue("Suggestions", out var suggObj) && suggObj is int sugg)
            score += Math.Min(sugg * 0.05, 0.3);
        if (result.Metrics.TryGetValue("AverageCohesion", out var cohObj) && cohObj is double coh)
            score += (CodeAnalysisConstants.CONFIDENCE_MAX - coh) * 0.2;
        if (result.Metrics.TryGetValue("AverageCoupling", out var coupObj) && coupObj is double coup)
            score += coup * 0.2;
        return Math.Clamp(score, 0, CodeAnalysisConstants.CONFIDENCE_MAX);
    }

    public static RecommendationType MapScoreToRecommendation(
        (ScoreCategory Category, double Score) dominant,
        Dictionary<ScoreCategory, double> scores)
    {
        return dominant.Category switch
        {
            ScoreCategory.Architecture => RecommendationType.ArchitectureRefactoring,
            ScoreCategory.Method => RecommendationType.MethodRefactoring,
            ScoreCategory.Variable => RecommendationType.VariableOptimization,
            ScoreCategory.Stability => RecommendationType.BugFix,
            ScoreCategory.Complexity => RecommendationType.ComplexityReduction,
            _ => RecommendationType.GeneralOptimization
        };
    }

    public static double CalculateConfidence(
        Dictionary<ScoreCategory, double> scores,
        (ScoreCategory Category, double Score) dominant)
    {
        double baseConfidence = dominant.Score * 0.6;
        double supportingScore = scores.Where(kv => kv.Key != dominant.Category)
            .Average(kv => kv.Value);
        return Math.Min(baseConfidence + supportingScore * 0.4, CodeAnalysisConstants.CONFIDENCE_MAX);
    }

    public static string GetScoreCategoryName(ScoreCategory category) => category switch
    {
        ScoreCategory.Architecture => "架构",
        ScoreCategory.Method => "方法",
        ScoreCategory.Variable => "变量",
        ScoreCategory.Stability => "稳定性",
        ScoreCategory.Complexity => "复杂度",
        ScoreCategory.General => "通用",
        _ => "未知"
    };
}
