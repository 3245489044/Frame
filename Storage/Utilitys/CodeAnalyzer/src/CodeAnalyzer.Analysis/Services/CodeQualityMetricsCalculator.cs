using CodeAnalyzer.Core.Models;

namespace CodeAnalyzer.Analysis.Services;

public static class CodeQualityMetricsCalculator
{
    public static Dictionary<string, object> CalculateEnhancedMetrics(
        List<EnhancedCodeElement> elements,
        List<CodeCluster> clusters,
        List<RefactoringSuggestion> suggestions)
    {
        var methods = elements.Where(e => e.Type == ElementType.Method).ToList();
        var elementsWithVariables = elements.Where(e => e.VariableCount > 0).ToList();

        double totalEffectiveVariableReuse = 0;
        int elementsWithVarCount = 0;

        foreach (var element in elementsWithVariables)
        {
            double reuseRatio = CalculateEffectiveVariableReuseRatio(element);
            if (reuseRatio > 0)
            {
                totalEffectiveVariableReuse += reuseRatio;
                elementsWithVarCount++;
            }
        }

        double averageVariableReuseRatio = 0.0;
        if (elementsWithVariables.Any())
        {
            var elementsWithValidReuseRatio = elementsWithVariables.Where(e => e.VariableReuseRatio > 0).ToList();
            if (elementsWithValidReuseRatio.Any())
            {
                averageVariableReuseRatio = elementsWithValidReuseRatio.Average(e => e.VariableReuseRatio);
            }
        }

        return new Dictionary<string, object>
        {
            ["TotalElements"] = elements.Count,
            ["Methods"] = elements.Count(e => e.Type == ElementType.Method),
            ["Clusters"] = clusters.Count,
            ["Suggestions"] = suggestions.Count,
            ["HighConfidenceSuggestions"] = suggestions.Count(s => s.Confidence > CodeAnalysisConstants.CONFIDENCE_HIGH),
            ["AverageCohesion"] = clusters.Any() ? clusters.Average(c => c.Cohesion) : 0,
            ["AverageCoupling"] = clusters.Any() ? clusters.Average(c => c.Coupling) : 0,
            ["TotalVariables"] = elements.Sum(e => e.VariableCount),
            ["AverageVariablesPerMethod"] = methods.Any() ? methods.Average(e => (double)e.VariableCount) : 0,
            ["AverageParametersPerMethod"] = methods.Any() ? methods.Average(e => (double)e.Parameters.Count) : 0,
            ["AverageVariableReuseRatio"] = averageVariableReuseRatio,
            ["EffectiveVariableReuseRatio"] = elementsWithVarCount > 0 ? totalEffectiveVariableReuse / elementsWithVarCount : 0,
            ["LargeClassCount"] = elements.Count(e => e.Type == ElementType.Class || e.Type == ElementType.Struct),
            ["HighComplexityMethods"] = methods.Count(m => m.ComplexityScore > CodeAnalysisConstants.HIGH_COMPLEXITY_THRESHOLD),
            ["AverageComplexity"] = methods.Any() ? methods.Average(m => m.ComplexityScore) : 0,
            ["BugFixSuggestions"] = suggestions.Count(s => s.Risks.Any(r => r.Contains("异常") || r.Contains("错误") || r.Contains("崩溃")))
        };
    }

    public static double CalculateEffectiveVariableReuseRatio(EnhancedCodeElement element)
    {
        if (element.VariableCount == 0) return 0;
        double totalReuse = 0;
        int countedVars = 0;
        foreach (var variable in element.Variables)
        {
            if (variable.UsageCount > 1)
            {
                totalReuse += (double)variable.UsageCount / (variable.UsageCount + 1);
                countedVars++;
            }
        }
        return countedVars > 0 ? totalReuse / countedVars : 0;
    }
}
