using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using RoslynSpace.RoslynSyntaxUtilities.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RoslynSpace.RoslynSyntaxUtilities.Base
{
    public class PromptRecommenderSkeleton
    {
        private readonly AnalysisConfiguration _defaultConfig;

        public PromptRecommenderSkeleton(AnalysisConfiguration config = null)
        {
            _defaultConfig = config ?? new AnalysisConfiguration
            {
                ConfidenceThreshold = CodeAnalysisConstants.CONFIDENCE_MEDIUM,
                DistanceThreshold = 5.0,
                MaxClusters = 8
            };
        }

        public RecommendedPrompt RecommendForCode(string code, AnalysisConfiguration config = null)
        {
            config = config ?? _defaultConfig;

            try
            {
                if (string.IsNullOrEmpty(code))
                    return CreateEmptyPrompt("输入代码为空");

                var result = PerformAnalysis(code, config);
                if (!string.IsNullOrEmpty(result.Error))
                    return CreateEmptyPrompt(result.Error);

                var suggestions = new RefactoringSuggestionSkeleton(config).GenerateSuggestions(result.Elements, result.Clusters, config);
                var assessment = OptimizationSkeleton.DetermineOptimizationAssessment(suggestions, result);

                if (assessment == OptimizationAssessment.NotRequired)
                    return CreateEmptyPrompt("无需优化");

                var analysis = PerformRecommendationAnalysis(result, suggestions, assessment);
                return BuildRecommendedPrompt(analysis, result, suggestions);
            }
            catch (Exception ex)
            {
                return CreateEmptyPrompt($"分析异常: {ex.Message}");
            }
        }

        public async Task<RecommendedPrompt> RecommendForCodeAsync(
            string code,
            AnalysisConfiguration config = null,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return RecommendForCode(code, config);
            }, cancellationToken);
        }

        public Dictionary<string, string> GetQuickMetrics(string code, AnalysisConfiguration config = null)
        {
            config = config ?? _defaultConfig;
            var metrics = new Dictionary<string, string>();

            if (string.IsNullOrEmpty(code))
            {
                metrics["状态"] = "输入代码为空";
                return metrics;
            }

            try
            {
                var result = PerformAnalysis(code, config);
                metrics["元素数量"] = result.Elements.Count.ToString();
                metrics["分析时间"] = $"{result.AnalysisTime:F2}ms";

                if (result.Elements.Any())
                {
                    metrics["平均复杂度"] = $"{result.Elements.Average(e => e.ComplexityScore):F2}";
                    metrics["最大复杂度"] = result.Elements.Max(e => e.ComplexityScore).ToString();
                }

                var suggestions = new RefactoringSuggestionSkeleton(config).GenerateSuggestions(result.Elements, result.Clusters, config);
                metrics["建议数量"] = suggestions.Count.ToString();

                var assessment = OptimizationSkeleton.DetermineOptimizationAssessment(suggestions, result);
                metrics["优化评估"] = OptimizationSkeleton.GetAssessmentDisplay(assessment);
            }
            catch (Exception ex)
            {
                metrics["错误"] = ex.Message;
            }

            return metrics;
        }

        private static AnalysisResult PerformAnalysis(string code, AnalysisConfiguration config)
        {
            var result = new AnalysisResult();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var syntaxTree = CSharpSyntaxTree.ParseText(code);
                var root = syntaxTree.GetRoot();
                var compilation = CSharpCompilation.Create("PromptAnalysis")
                    .AddReferences(MetadataReference.CreateFromFile(typeof(object).Assembly.Location))
                    .AddSyntaxTrees(syntaxTree);
                var semanticModel = compilation.GetSemanticModel(syntaxTree);

                var skeleton = new AnalysisSkeleton(semanticModel);
                var elements = skeleton.AnalyzeElements(root);

                if (elements.Count == 0)
                {
                    result.Error = "未找到可分析的代码元素";
                    return result;
                }

                result.Elements = elements;
                result.AnalysisTime = sw.Elapsed.TotalMilliseconds;
            }
            catch (Exception ex)
            {
                result.Error = $"分析过程异常: {ex.Message}";
            }

            return result;
        }

        private RecommendationAnalysis PerformRecommendationAnalysis(
            AnalysisResult result,
            List<RefactoringSuggestion> suggestions,
            OptimizationAssessment assessment)
        {
            var analysis = new RecommendationAnalysis();

            var architectureScore = CalculateArchitectureScore(result, suggestions);
            var methodScore = CalculateMethodScore(result, suggestions);
            var variableScore = CalculateVariableScore(result, suggestions);
            var complexityScore = CalculateComplexityScore(result, suggestions);

            analysis.Scores[ScoreCategory.Architecture] = architectureScore;
            analysis.Scores[ScoreCategory.Method] = methodScore;
            analysis.Scores[ScoreCategory.Variable] = variableScore;
            analysis.Scores[ScoreCategory.Complexity] = complexityScore;

            var dominant = analysis.Scores.OrderByDescending(s => s.Value).First();
            analysis.DominantScore = (dominant.Key, dominant.Value);

            analysis.RecommendationType = DetermineRecommendationType(analysis.DominantScore.Category, assessment);

            analysis.Confidence = CalculateOverallConfidence(analysis, assessment);

            analysis.AnalysisMetrics["元素数量"] = result.Elements.Count;
            analysis.AnalysisMetrics["建议数量"] = suggestions.Count;
            analysis.AnalysisMetrics["架构分数"] = architectureScore;
            analysis.AnalysisMetrics["方法分数"] = methodScore;
            analysis.AnalysisMetrics["变量分数"] = variableScore;
            analysis.AnalysisMetrics["复杂度分数"] = complexityScore;

            return analysis;
        }

        private RecommendedPrompt BuildRecommendedPrompt(
            RecommendationAnalysis analysis,
            AnalysisResult result,
            List<RefactoringSuggestion> suggestions)
        {
            var prompt = new RecommendedPrompt
            {
                Confidence = analysis.Confidence,
                Reason = BuildReason(analysis),
                AnalysisDetails = new Dictionary<string, object>
                {
                    ["评估"] = OptimizationSkeleton.GetAssessmentDisplay(
                        OptimizationSkeleton.DetermineOptimizationAssessment(suggestions, result)),
                    ["建议数量"] = suggestions.Count,
                    ["主导类别"] = analysis.DominantScore.Category.ToString(),
                    ["主导分数"] = analysis.DominantScore.Score,
                    ["推荐类型"] = analysis.RecommendationType.ToString(),
                    ["置信度"] = analysis.Confidence
                }
            };

            prompt.Prompt = BuildPromptText(analysis, result, suggestions);

            return prompt;
        }

        private string BuildPromptText(RecommendationAnalysis analysis, AnalysisResult result, List<RefactoringSuggestion> suggestions)
        {
            var parts = new List<string>();

            parts.Add("基于代码分析结果，建议进行以下优化：\n");

            var grouped = suggestions.GroupBy(s => s.Category);
            foreach (var group in grouped)
            {
                parts.Add($"## {group.Key}");
                foreach (var suggestion in group)
                {
                    parts.Add($"- {suggestion.DetailedDescription} (置信度: {suggestion.Confidence:P0})");
                }
                parts.Add("");
            }

            parts.Add("## 分析指标");
            parts.Add($"- 代码元素数量: {result.Elements.Count}");
            parts.Add($"- 分析耗时: {result.AnalysisTime:F2}ms");

            if (analysis.AnalysisMetrics.Count > 0)
            {
                parts.Add("## 推荐分析");
                foreach (var metric in analysis.AnalysisMetrics)
                {
                    parts.Add($"- {metric.Key}: {metric.Value}");
                }
            }

            return string.Join("\n", parts);
        }

        private string BuildReason(RecommendationAnalysis analysis)
        {
            var reasons = new List<string>();

            reasons.Add($"主导优化方向: {analysis.DominantScore.Category} (分数: {analysis.DominantScore.Score:F2})");
            reasons.Add($"推荐类型: {analysis.RecommendationType}");
            reasons.Add($"置信度: {analysis.Confidence:P0}");

            return string.Join("; ", reasons);
        }

        private static double CalculateArchitectureScore(AnalysisResult result, List<RefactoringSuggestion> suggestions)
        {
            double score = 0;
            if (result.Clusters.Count > 0)
            {
                var avgCohesion = result.Clusters.Average(c => c.Cohesion);
                score += avgCohesion * CodeAnalysisConstants.ARCHITECTURE_SCORE_WEIGHT;
            }
            score += suggestions.Count(s => s.Type == RefactoringType.ExtractClass) * 0.1;
            return Math.Min(score, 1.0);
        }

        private static double CalculateMethodScore(AnalysisResult result, List<RefactoringSuggestion> suggestions)
        {
            double score = 0;
            var methodSuggestions = suggestions.Where(s => s.Type == RefactoringType.ExtractMethod).ToList();
            score += methodSuggestions.Count * CodeAnalysisConstants.METHOD_REFACTORING_WEIGHT;

            var highComplexityMethods = result.Elements.Count(e =>
                e.Type == ElementType.Method && e.ComplexityScore > CodeAnalysisConstants.HIGH_COMPLEXITY_THRESHOLD);
            score += highComplexityMethods * 0.15;

            return Math.Min(score, 1.0);
        }

        private static double CalculateVariableScore(AnalysisResult result, List<RefactoringSuggestion> suggestions)
        {
            double score = 0;
            var methods = result.Elements.Where(e => e.Type == ElementType.Method).ToList();

            foreach (var method in methods)
            {
                if (method.VariableReuseRatio < CodeAnalysisConstants.LOW_VARIABLE_REUSE_THRESHOLD)
                    score += CodeAnalysisConstants.VARIABLE_REUSE_WEIGHT * 0.1;
                if (method.Variables.Count > CodeAnalysisConstants.MANY_LOCAL_VARIABLES_THRESHOLD)
                    score += 0.1;
            }

            return Math.Min(score, 1.0);
        }

        private static double CalculateComplexityScore(AnalysisResult result, List<RefactoringSuggestion> suggestions)
        {
            double score = 0;
            var methods = result.Elements.Where(e => e.Type == ElementType.Method).ToList();

            if (methods.Count == 0) return 0;

            var avgComplexity = methods.Average(e => e.ComplexityScore);
            score = Math.Min(avgComplexity / CodeAnalysisConstants.HIGH_COMPLEXITY_SCORE_DIVISOR,
                CodeAnalysisConstants.HIGH_COMPLEXITY_SCORE_MAX);

            return score;
        }

        private static RecommendationType DetermineRecommendationType(ScoreCategory dominantCategory, OptimizationAssessment assessment)
        {
            if (assessment == OptimizationAssessment.Critical || assessment == OptimizationAssessment.HighPriority)
            {
                return dominantCategory switch
                {
                    ScoreCategory.Architecture => RecommendationType.ArchitectureRefactoring,
                    ScoreCategory.Method => RecommendationType.MethodRefactoring,
                    ScoreCategory.Variable => RecommendationType.VariableOptimization,
                    ScoreCategory.Complexity => RecommendationType.ComplexityReduction,
                    _ => RecommendationType.GeneralOptimization
                };
            }

            return RecommendationType.GeneralOptimization;
        }

        private static double CalculateOverallConfidence(RecommendationAnalysis analysis, OptimizationAssessment assessment)
        {
            double baseConfidence = assessment switch
            {
                OptimizationAssessment.Critical => CodeAnalysisConstants.CONFIDENCE_VERY_HIGH,
                OptimizationAssessment.HighPriority => CodeAnalysisConstants.CONFIDENCE_HIGH,
                OptimizationAssessment.MediumPriority => CodeAnalysisConstants.CONFIDENCE_MEDIUM,
                OptimizationAssessment.LowPriority => CodeAnalysisConstants.CONFIDENCE_LOW,
                _ => 0
            };

            double scoreBonus = analysis.DominantScore.Score * 0.2;
            return Math.Min(baseConfidence + scoreBonus, CodeAnalysisConstants.CONFIDENCE_MAX);
        }

        private static RecommendedPrompt CreateEmptyPrompt(string reason) => new RecommendedPrompt
        {
            Prompt = "",
            Confidence = 0.0,
            Reason = reason,
            AnalysisDetails = new Dictionary<string, object> { ["Status"] = reason }
        };
    }
}
