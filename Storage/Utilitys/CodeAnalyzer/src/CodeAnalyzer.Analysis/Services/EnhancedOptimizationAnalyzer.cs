using CodeAnalyzer.Core.Models;

namespace CodeAnalyzer.Analysis.Services;

public static class EnhancedOptimizationAnalyzer
{
    public static (OptimizationAssessment Assessment, string Report, RecommendedPrompt Prompt) AnalyzeWithRecommendation(
        string code, AnalysisConfiguration config)
    {
        try
        {
            if (string.IsNullOrEmpty(code))
                return (OptimizationAssessment.NotRequired, "分析失败: 输入代码为空", CreateErrorPrompt("输入代码为空"));

            var result = CodeAnalysisEngine.Analyze(code, config);
            if (!string.IsNullOrEmpty(result.Error))
                return (OptimizationAssessment.NotRequired, $"分析失败: {result.Error}", CreateErrorPrompt(result.Error));

            var suggestions = RefactoringSuggestionGenerator.GenerateSuggestions(result.Elements, result.Clusters, config);
            var assessment = OptimizationAssessmentCalculator.DetermineOptimizationAssessment(suggestions, result);
            string report = AnalysisReportGenerator.GenerateReport(result, suggestions, assessment);
            RecommendedPrompt recommendedPrompt;

            if (assessment != OptimizationAssessment.NotRequired)
            {
                recommendedPrompt = new RecommendedPrompt
                {
                    Prompt = "请分析以下代码并提供优化建议，包括提取通用方法、简化复杂逻辑、优化变量使用等。",
                    Confidence = 0.6,
                    Reason = "检测到优化机会",
                    AnalysisDetails = new Dictionary<string, object>
                    {
                        ["Assessment"] = assessment.ToString(),
                        ["TotalSuggestions"] = suggestions.Count
                    }
                };
                report = EnhanceReportWithPrompt(report, recommendedPrompt, assessment);
            }
            else
            {
                recommendedPrompt = new RecommendedPrompt
                {
                    Prompt = "",
                    Confidence = 0.0,
                    Reason = "无需优化",
                    AnalysisDetails = new Dictionary<string, object> { ["Assessment"] = "NotRequired" }
                };
            }

            return (assessment, report, recommendedPrompt);
        }
        catch (Exception ex)
        {
            return (OptimizationAssessment.NotRequired, $"分析异常: {ex.Message}", CreateErrorPrompt(ex.Message));
        }
    }

    private static RecommendedPrompt CreateErrorPrompt(string error) => new()
    {
        Prompt = "系统分析过程中出现错误，请检查代码格式或重试。",
        Confidence = 0.0,
        Reason = $"分析异常: {error}",
        AnalysisDetails = new Dictionary<string, object> { ["Exception"] = error }
    };

    private static string EnhanceReportWithPrompt(string original, RecommendedPrompt prompt, OptimizationAssessment assessment)
    {
        if (assessment == OptimizationAssessment.NotRequired || string.IsNullOrEmpty(prompt?.Prompt))
            return original;

        var sb = new System.Text.StringBuilder(original);
        sb.AppendLine().AppendLine("--- AI优化执行指令 ---")
          .AppendLine($"请执行以下指令：{prompt.Prompt}")
          .AppendLine($"该建议置信度：{prompt.Confidence:P1}")
          .AppendLine($"优化理由：{prompt.Reason}");
        return sb.ToString();
    }
}
