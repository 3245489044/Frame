using System.Text;
using CodeAnalyzer.Core.Models;

namespace CodeAnalyzer.Analysis.Services;

public static class AnalysisReportGenerator
{
    public static string GenerateReport(AnalysisResult result, List<RefactoringSuggestion> suggestions, OptimizationAssessment assessment)
    {
        var sb = new StringBuilder();

        sb.AppendLine("请根据以下代码分析报告，对代码进行全面优化：")
          .AppendLine("---")
          .AppendLine($"分析耗时：{result.AnalysisTime:F2}秒。")
          .AppendLine($"当前代码优化评估等级为：{OptimizationAssessmentCalculator.GetAssessmentDisplay(assessment)}。请优先处理低等级问题。")
          .AppendLine($"评估详细说明：{OptimizationAssessmentCalculator.GetAssessmentDescription(assessment)}。需重点关注描述中的缺陷。")
          .AppendLine()
          .AppendLine("代码概览信息：")
          .AppendLine($"代码元素总数：{result.Elements.Count}。");

        var dist = result.Elements.GroupBy(e => e.Type).Take(5)
                     .Select(g => $"{g.Key}:{g.Count()}");
        sb.AppendLine($"元素类型分布：{string.Join("; ", dist)}。").AppendLine()
          .AppendLine("关键质量指标（供优化参考）：");

        if (result.Metrics.TryGetValue("AverageComplexity", out var c))
            sb.AppendLine($"- 平均圈复杂度：{c:F1}。目标值应低于10。");
        if (result.Metrics.TryGetValue("AverageVariableReuseRatio", out var r))
            sb.AppendLine($"- 变量平均重用率：{r:P0}。建议提升至50%以上。");
        if (result.Metrics.TryGetValue("AverageParametersPerMethod", out var p))
            sb.AppendLine($"- 方法平均参数数：{p:F1}。超过3个时考虑封装。");
        if (result.Metrics.TryGetValue("Clusters", out var cl))
            sb.AppendLine($"- 代码聚类数量：{cl}。");
        if (result.Metrics.TryGetValue("AverageCohesion", out var ch))
            sb.AppendLine($"- 聚类平均内聚度：{ch:F2}。");
        if (result.Metrics.TryGetValue("AverageCoupling", out var cp))
            sb.AppendLine($"- 聚类平均耦合度：{cp:F2}。");

        sb.AppendLine();
        if (!suggestions.Any()) return sb.ToString();

        sb.AppendLine().AppendLine("具体优化建议（请逐一实施）：");
        foreach (var g in suggestions.GroupBy(s => s.Category).OrderByDescending(g => g.Max(s => s.Confidence)))
        {
            if (g.Key == "变量重用率优化")
            {
                sb.AppendLine().AppendLine($"变量重用率优化 ({g.Count()}条):")
                  .AppendLine("| 变量重用率 | 低使用变量 | 置信度 |")
                  .AppendLine("|------------|------------|--------|");
                foreach (var s in g.OrderByDescending(s => s.Confidence))
                {
                    var ratioMatch = System.Text.RegularExpressions.Regex.Match(s.DetailedDescription, @"变量重用率仅(\d+)%");
                    var varsMatch = System.Text.RegularExpressions.Regex.Match(s.DetailedDescription, @"低使用变量:\s*(.+)");
                    sb.AppendLine($"| {(ratioMatch.Success ? ratioMatch.Groups[1].Value + "%" : "N/A")} | {(varsMatch.Success ? varsMatch.Groups[1].Value : "")} | {(s.Confidence * 100):F1}% |");
                }
            }
            else
            {
                sb.AppendLine().AppendLine($"### {g.Key} ({g.Count()}条):");
                foreach (var s in g.OrderByDescending(s => s.Confidence))
                {
                    string tag = s.Confidence >= CodeAnalysisConstants.CONFIDENCE_HIGH ? "[置信度高（强烈推荐）]"
                              : s.Confidence >= CodeAnalysisConstants.CONFIDENCE_MEDIUM ? "[置信度中（建议实施）]"
                              : "[置信度低（可选择性优化）]";
                    sb.Append($"  - {tag} {s.DetailedDescription}");
                    if (s.OptimizationType == RefactoringTypeNames.METHOD_OPTIMIZATION && s.ElementNames.Any())
                        sb.Append($" : （方法：{s.ElementNames.First()}）");
                    else if (s.OptimizationType == RefactoringTypeNames.VARIABLE_OPTIMIZATION && s.VariableDetails.Any())
                        sb.Append($" : （变量：{string.Join("、", s.VariableDetails.Take(2).Select(v => v.VariableName))}）");
                    sb.AppendLine();
                }
            }
        }
        return sb.ToString();
    }
}
