using CodeAnalyzer.Core.Models;

namespace CodeAnalyzer.Analysis.Services;

public static class RefactoringSuggestionGenerator
{
    public static List<RefactoringSuggestion> GenerateSuggestions(
        List<EnhancedCodeElement> elements,
        List<CodeCluster> clusters,
        AnalysisConfiguration config)
    {
        var suggestions = new List<RefactoringSuggestion>();
        if (elements == null || elements.Count == 0) return suggestions;

        suggestions.AddRange(DetectLargeClasses(elements));

        suggestions.AddRange(DetectHighComplexityMethods(elements));

        suggestions.AddRange(DetectLongMethods(elements));

        suggestions.AddRange(DetectManyParameters(elements));

        suggestions.AddRange(DetectLowVariableReuse(elements));

        suggestions.AddRange(DetectLowCohesionClusters(clusters));

        suggestions.AddRange(DetectHighCouplingClusters(clusters));

        return suggestions;
    }

    private static List<RefactoringSuggestion> DetectLargeClasses(List<EnhancedCodeElement> elements)
    {
        var suggestions = new List<RefactoringSuggestion>();
        var classGroups = elements.Where(e => e.Type == ElementType.Class || e.Type == ElementType.Struct)
            .GroupBy(e => e.Name);

        foreach (var group in classGroups)
        {
            if (group.Count() > CodeAnalysisConstants.LARGE_CLASS_ELEMENTS_THRESHOLD)
            {
                var suggestion = new RefactoringSuggestion(
                    Guid.NewGuid().ToString(),
                    RefactoringType.ExtractClass)
                {
                    Confidence = Math.Min(0.5 + (group.Count() - CodeAnalysisConstants.LARGE_CLASS_ELEMENTS_THRESHOLD) * 0.05, 0.95),
                    Category = "大型类拆分",
                    DetailedDescription = $"类 '{group.Key}' 包含 {group.Count()} 个成员，建议拆分为多个小类",
                    OptimizationType = RefactoringTypeNames.CLASS_EXTRACTION,
                    ElementNames = new List<string> { group.Key }
                };
                suggestions.Add(suggestion);
            }
        }
        return suggestions;
    }

    private static List<RefactoringSuggestion> DetectHighComplexityMethods(List<EnhancedCodeElement> elements)
    {
        var suggestions = new List<RefactoringSuggestion>();
        var methods = elements.Where(e => e.Type == ElementType.Method);

        foreach (var method in methods)
        {
            if (method.ComplexityScore > CodeAnalysisConstants.HIGH_COMPLEXITY_THRESHOLD)
            {
                var suggestion = new RefactoringSuggestion(
                    Guid.NewGuid().ToString(),
                    RefactoringType.ExtractMethod)
                {
                    Confidence = Math.Min(0.5 + (method.ComplexityScore - CodeAnalysisConstants.HIGH_COMPLEXITY_THRESHOLD) * 0.03, 0.9),
                    Category = "高复杂度方法",
                    DetailedDescription = $"方法 '{method.Name}' 圈复杂度为 {method.ComplexityScore}，建议提取子方法简化逻辑",
                    OptimizationType = RefactoringTypeNames.METHOD_OPTIMIZATION,
                    ElementNames = new List<string> { method.Name }
                };
                suggestions.Add(suggestion);
            }
        }
        return suggestions;
    }

    private static List<RefactoringSuggestion> DetectLongMethods(List<EnhancedCodeElement> elements)
    {
        var suggestions = new List<RefactoringSuggestion>();
        var methods = elements.Where(e => e.Type == ElementType.Method);

        foreach (var method in methods)
        {
            if (method.BodyLines > CodeAnalysisConstants.LONG_METHOD_LINES_THRESHOLD)
            {
                var suggestion = new RefactoringSuggestion(
                    Guid.NewGuid().ToString(),
                    RefactoringType.ExtractMethod)
                {
                    Confidence = Math.Min(0.4 + (method.BodyLines - CodeAnalysisConstants.LONG_METHOD_LINES_THRESHOLD) * 0.01, 0.85),
                    Category = "长方法",
                    DetailedDescription = $"方法 '{method.Name}' 有 {method.BodyLines} 行代码，建议提取子方法",
                    OptimizationType = RefactoringTypeNames.METHOD_OPTIMIZATION,
                    ElementNames = new List<string> { method.Name }
                };
                suggestions.Add(suggestion);
            }
        }
        return suggestions;
    }

    private static List<RefactoringSuggestion> DetectManyParameters(List<EnhancedCodeElement> elements)
    {
        var suggestions = new List<RefactoringSuggestion>();
        var methods = elements.Where(e => e.Type == ElementType.Method);

        foreach (var method in methods)
        {
            if (method.ParameterCount > CodeAnalysisConstants.TOO_MANY_PARAMETERS_THRESHOLD)
            {
                var suggestion = new RefactoringSuggestion(
                    Guid.NewGuid().ToString(),
                    RefactoringType.IntroduceParameterObject)
                {
                    Confidence = Math.Min(0.5 + (method.ParameterCount - CodeAnalysisConstants.TOO_MANY_PARAMETERS_THRESHOLD) * 0.1, 0.9),
                    Category = "参数过多",
                    DetailedDescription = $"方法 '{method.Name}' 有 {method.ParameterCount} 个参数，建议引入参数对象",
                    OptimizationType = RefactoringTypeNames.PARAMETER_OBJECT_EXTRACTION,
                    ElementNames = new List<string> { method.Name }
                };
                suggestions.Add(suggestion);
            }
        }
        return suggestions;
    }

    private static List<RefactoringSuggestion> DetectLowVariableReuse(List<EnhancedCodeElement> elements)
    {
        var suggestions = new List<RefactoringSuggestion>();
        var elementsWithVars = elements.Where(e => e.VariableCount > 0);

        foreach (var element in elementsWithVars)
        {
            if (element.VariableReuseRatio < CodeAnalysisConstants.LOW_VARIABLE_REUSE_THRESHOLD)
            {
                var lowUsageVars = element.Variables.Where(v => v.UsageCount <= 1).ToList();
                var suggestion = new RefactoringSuggestion(
                    Guid.NewGuid().ToString(),
                    RefactoringType.ExtractMethod)
                {
                    Confidence = 0.6,
                    Category = "变量重用率优化",
                    DetailedDescription = $"变量重用率仅{(element.VariableReuseRatio * 100):F0}%，低使用变量: {string.Join(", ", lowUsageVars.Take(3).Select(v => v.Name))}",
                    OptimizationType = RefactoringTypeNames.VARIABLE_OPTIMIZATION,
                    ElementNames = new List<string> { element.Name },
                    VariableDetails = lowUsageVars.Select(v => new VariableOptimizationDetail
                    {
                        VariableName = v.Name,
                        UsageCount = v.UsageCount,
                        IsParameter = v.IsParameter,
                        Type = v.Type
                    }).ToList()
                };
                suggestions.Add(suggestion);
            }
        }
        return suggestions;
    }

    private static List<RefactoringSuggestion> DetectLowCohesionClusters(List<CodeCluster> clusters)
    {
        var suggestions = new List<RefactoringSuggestion>();
        foreach (var cluster in clusters)
        {
            if (cluster.Cohesion < CodeAnalysisConstants.LOW_COHESION_THRESHOLD && cluster.Elements.Count > 1)
            {
                var suggestion = new RefactoringSuggestion(
                    Guid.NewGuid().ToString(),
                    RefactoringType.SplitClass)
                {
                    Confidence = 0.7,
                    Category = "低内聚聚类",
                    DetailedDescription = $"聚类 '{cluster.Name}' 内聚度仅 {cluster.Cohesion:F2}，建议重新组织代码结构",
                    OptimizationType = RefactoringTypeNames.CLASS_EXTRACTION,
                    ElementNames = cluster.Elements.Select(e => e.Name).Take(5).ToList()
                };
                suggestions.Add(suggestion);
            }
        }
        return suggestions;
    }

    private static List<RefactoringSuggestion> DetectHighCouplingClusters(List<CodeCluster> clusters)
    {
        var suggestions = new List<RefactoringSuggestion>();
        foreach (var cluster in clusters)
        {
            if (cluster.Coupling > CodeAnalysisConstants.HIGH_COUPLING_THRESHOLD && cluster.Elements.Count > 1)
            {
                var suggestion = new RefactoringSuggestion(
                    Guid.NewGuid().ToString(),
                    RefactoringType.ExtractInterface)
                {
                    Confidence = 0.6,
                    Category = "高耦合聚类",
                    DetailedDescription = $"聚类 '{cluster.Name}' 耦合度为 {cluster.Coupling:F2}，建议通过接口解耦",
                    OptimizationType = RefactoringTypeNames.CLASS_EXTRACTION,
                    ElementNames = cluster.Elements.Select(e => e.Name).Take(5).ToList()
                };
                suggestions.Add(suggestion);
            }
        }
        return suggestions;
    }
}

public static class RefactoringTypeNames
{
    public const string METHOD_OPTIMIZATION = "MethodOptimization";
    public const string CLASS_EXTRACTION = "ClassExtraction";
    public const string VARIABLE_OPTIMIZATION = "VariableOptimization";
    public const string PARAMETER_OBJECT_EXTRACTION = "ParameterObjectExtraction";
}
