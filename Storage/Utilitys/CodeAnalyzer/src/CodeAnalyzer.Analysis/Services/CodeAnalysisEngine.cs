using System.Diagnostics;
using CodeAnalyzer.Core.Models;

namespace CodeAnalyzer.Analysis.Services;

public static class CodeAnalysisEngine
{
    public static AnalysisResult Analyze(string code, AnalysisConfiguration config)
    {
        var sw = Stopwatch.StartNew();
        var result = new AnalysisResult();

        try
        {
            if (string.IsNullOrEmpty(code))
                return AnalysisResult.WithError("输入代码为空");

            var ctx = new AnalysisContext(code);
            if (ctx.Root == null)
                return AnalysisResult.WithError("无法解析代码语法树");

            var elementExtractor = new CodeElementExtractor(ctx);
            result.Elements = elementExtractor.ExtractElements();

            if (result.Elements.Count == 0)
                return AnalysisResult.Empty("未找到任何代码元素");

            result.Distances = DistanceCalculator.CalculateDistances(result.Elements, config);

            result.Clusters = ClusterAnalyzer.ClusterElements(result.Elements, result.Distances, config);

            result.Suggestions = RefactoringSuggestionGenerator.GenerateSuggestions(result.Elements, result.Clusters, config);

            result.Metrics = CodeQualityMetricsCalculator.CalculateEnhancedMetrics(result.Elements, result.Clusters, result.Suggestions);

            sw.Stop();
            result.AnalysisTime = sw.Elapsed.TotalSeconds;
        }
        catch (Exception ex)
        {
            result.Error = $"分析异常: {ex.Message}";
        }

        return result;
    }
}
