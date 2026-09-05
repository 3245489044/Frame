using CodeAnalyzer.Analysis.Services;
using CodeAnalyzer.Core.Models;

namespace CodeAnalyzer.Prompt.Services;

public class PromptQualityContext
{
    public IReadOnlyDictionary<string, object> Metrics { get; }
    public IReadOnlyDictionary<ScoreCategory, double> CategoryScores { get; }
    public OptimizationAssessment Assessment { get; }
    public string AssessmentDisplay { get; }
    public string AssessmentDescription { get; }
    public IReadOnlyList<RefactoringSuggestion> Suggestions { get; }
    public int TotalSuggestions => Suggestions.Count;
    public int HighConfidenceSuggestions => Suggestions.Count(s => s.Confidence >= CodeAnalysisConstants.CONFIDENCE_HIGH);
    public AnalysisResult RawResult { get; }

    public PromptQualityContext(AnalysisResult result, OptimizationAssessment assessment = OptimizationAssessment.NotRequired)
    {
        RawResult = result ?? throw new ArgumentNullException(nameof(result));
        Metrics = result.Metrics ?? new Dictionary<string, object>();
        Suggestions = result.Suggestions?.ToList() ?? new List<RefactoringSuggestion>();
        Assessment = assessment;
        AssessmentDisplay = OptimizationAssessmentCalculator.GetAssessmentDisplay(assessment);
        AssessmentDescription = OptimizationAssessmentCalculator.GetAssessmentDescription(assessment);
        CategoryScores = RecommendationAnalyzer.CalculateComprehensiveScores(result);
    }

    public T GetMetric<T>(string key, T defaultValue = default) =>
        Metrics.TryGetValue(key, out var v) && v is T t ? t : defaultValue;

    public double GetCategoryScore(ScoreCategory category) =>
        CategoryScores.TryGetValue(category, out var s) ? s : 0.0;

    public (ScoreCategory Category, double Score) GetDominantCategory() =>
        CategoryScores?.Count > 0
            ? CategoryScores.OrderByDescending(kv => kv.Value).First() switch
            {
                var (key, value) => (key, value)
            }
            : (ScoreCategory.General, 0.0);
}
