using CodeAnalyzer.Analysis.Services;
using CodeAnalyzer.Core.Models;

namespace CodeAnalyzer.Analysis.Refactoring.Skeletons;

public static class RefactoringSuggestionSkeleton
{
    public static List<RefactoringSuggestion> GenerateSuggestions(
        List<EnhancedCodeElement> elements,
        List<CodeCluster> clusters,
        AnalysisConfiguration? config = null)
    {
        config ??= new AnalysisConfiguration();
        return RefactoringSuggestionGenerator.GenerateSuggestions(elements, clusters, config);
    }

    public static Dictionary<string, List<RefactoringSuggestion>> GroupByCategory(List<RefactoringSuggestion> suggestions)
    {
        return suggestions.GroupBy(s => s.Category)
            .ToDictionary(g => g.Key, g => g.ToList());
    }
}
