using RoslynSpace.RoslynSyntaxUtilities.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace RoslynSpace.RoslynSyntaxUtilities.Base
{
    public class AnalysisReportSkeleton
    {
        public string GenerateReport(
            AnalysisResult result,
            List<RefactoringSuggestion> suggestions,
            OptimizationAssessment assessment)
        {
            if (result == null)
                return "分析结果为空";

            var sb = new StringBuilder();

            sb.AppendLine("=" .PadRight(60, '='));
            sb.AppendLine("代码分析报告");
            sb.AppendLine("=" .PadRight(60, '='));
            sb.AppendLine();

            AppendBasicInfo(sb, result);

            AppendAssessment(sb, assessment);

            AppendClusterAnalysis(sb, result);

            AppendSuggestions(sb, suggestions);

            AppendDetailedMetrics(sb, result);

            return sb.ToString();
        }

        public async Task<string> GenerateReportAsync(
            AnalysisResult result,
            List<RefactoringSuggestion> suggestions,
            OptimizationAssessment assessment,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return GenerateReport(result, suggestions, assessment);
            }, cancellationToken);
        }

        public string EnhanceReportWithPrompt(
            string report,
            RecommendedPrompt prompt,
            OptimizationAssessment assessment)
        {
            if (string.IsNullOrEmpty(report))
                return report;

            var sb = new StringBuilder(report);

            sb.AppendLine();
            sb.AppendLine("-" .PadRight(40, '-'));
            sb.AppendLine("优化推荐");
            sb.AppendLine("-" .PadRight(40, '-'));
            sb.AppendLine();

            if (prompt != null && !string.IsNullOrEmpty(prompt.Prompt))
            {
                sb.AppendLine(prompt.Prompt);
                sb.AppendLine();
                sb.AppendLine($"推荐置信度: {prompt.Confidence:P0}");
                sb.AppendLine($"推荐理由: {prompt.Reason}");
            }

            return sb.ToString();
        }

        public string GetAssessmentDisplay(OptimizationAssessment assessment)
        {
            return assessment switch
            {
                OptimizationAssessment.NotRequired => "无需优化",
                OptimizationAssessment.LowPriority => "低优先级优化",
                OptimizationAssessment.MediumPriority => "中等优先级优化",
                OptimizationAssessment.HighPriority => "高优先级优化",
                OptimizationAssessment.Critical => "关键优化",
                OptimizationAssessment.CriticalPriority => "紧急优化",
                _ => "未知"
            };
        }

        private static void AppendBasicInfo(StringBuilder sb, AnalysisResult result)
        {
            sb.AppendLine("【基本信息】");
            sb.AppendLine($"  分析时间: {result.AnalysisTime:F2}ms");
            sb.AppendLine($"  代码元素数量: {result.Elements.Count}");
            sb.AppendLine($"  聚类数量: {result.Clusters.Count}");
            sb.AppendLine($"  距离矩阵大小: {result.Distances.Count}");
            sb.AppendLine();
        }

        private static void AppendAssessment(StringBuilder sb, OptimizationAssessment assessment)
        {
            var display = assessment switch
            {
                OptimizationAssessment.NotRequired => "无需优化",
                OptimizationAssessment.LowPriority => "低优先级优化",
                OptimizationAssessment.MediumPriority => "中等优先级优化",
                OptimizationAssessment.HighPriority => "高优先级优化",
                OptimizationAssessment.Critical => "关键优化",
                OptimizationAssessment.CriticalPriority => "紧急优化",
                _ => "未知"
            };

            sb.AppendLine("【优化评估】");
            sb.AppendLine($"  评估结果: {display}");
            sb.AppendLine();
        }

        private static void AppendClusterAnalysis(StringBuilder sb, AnalysisResult result)
        {
            if (result.Clusters == null || result.Clusters.Count == 0)
                return;

            sb.AppendLine("【聚类分析】");
            foreach (var cluster in result.Clusters)
            {
                sb.AppendLine($"  聚类 {cluster.Id}: {cluster.Name}");
                sb.AppendLine($"    元素数量: {cluster.Elements.Count}");
                sb.AppendLine($"    内聚度: {cluster.Cohesion:F2}");
                sb.AppendLine($"    耦合度: {cluster.Coupling:F2}");

                if (cluster.Elements.Any())
                {
                    sb.AppendLine($"    包含元素: {string.Join(", ", cluster.Elements.Select(e => e.Name))}");
                }
                sb.AppendLine();
            }
        }

        private static void AppendSuggestions(StringBuilder sb, List<RefactoringSuggestion> suggestions)
        {
            if (suggestions == null || suggestions.Count == 0)
            {
                sb.AppendLine("【重构建议】");
                sb.AppendLine("  未发现需要重构的问题。");
                sb.AppendLine();
                return;
            }

            sb.AppendLine("【重构建议】");
            sb.AppendLine($"  共 {suggestions.Count} 条建议");

            var grouped = suggestions.GroupBy(s => s.Category);
            foreach (var group in grouped)
            {
                sb.AppendLine();
                sb.AppendLine($"  [{group.Key}]");
                foreach (var suggestion in group)
                {
                    sb.AppendLine($"    - {suggestion.DetailedDescription}");
                    sb.AppendLine($"      置信度: {suggestion.Confidence:P0}");

                    if (suggestion.Risks.Any())
                    {
                        sb.AppendLine($"      风险: {string.Join("; ", suggestion.Risks)}");
                    }
                }
            }
            sb.AppendLine();
        }

        private static void AppendDetailedMetrics(StringBuilder sb, AnalysisResult result)
        {
            if (result.Metrics == null || result.Metrics.Count == 0)
                return;

            sb.AppendLine("【详细指标】");
            foreach (var metric in result.Metrics)
            {
                sb.AppendLine($"  {metric.Key}: {metric.Value}");
            }
            sb.AppendLine();
        }
    }
}
