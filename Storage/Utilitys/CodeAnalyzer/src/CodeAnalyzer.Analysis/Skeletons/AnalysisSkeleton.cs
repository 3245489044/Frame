using CodeAnalyzer.Analysis.Services;
using CodeAnalyzer.Core.Models;

namespace CodeAnalyzer.Analysis.Skeletons;

public static class AnalysisSkeleton
{
    public static AnalysisResult Analyze(string code, AnalysisConfiguration? config = null)
    {
        config ??= new AnalysisConfiguration();
        return CodeAnalysisEngine.Analyze(code, config);
    }

    public static (List<EnhancedCodeElement> Elements, Dictionary<string, object> Metrics) AnalyzeSimple(string code, AnalysisConfiguration? config = null)
    {
        var result = Analyze(code, config);
        return (result.Elements, result.Metrics);
    }
}
