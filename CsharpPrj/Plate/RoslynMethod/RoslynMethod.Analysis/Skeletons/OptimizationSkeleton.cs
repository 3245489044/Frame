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
    public class OptimizationSkeleton
    {
        private readonly AnalysisConfiguration _defaultConfig;

        public OptimizationSkeleton(AnalysisConfiguration config = null)
        {
            _defaultConfig = config ?? new AnalysisConfiguration
            {
                ConfidenceThreshold = CodeAnalysisConstants.CONFIDENCE_MEDIUM,
                DistanceThreshold = 5.0,
                MaxClusters = 8
            };
        }

        public (OptimizationAssessment Assessment, string Report, RecommendedPrompt Prompt) AnalyzeWithRecommendation(
            string code,
            AnalysisConfiguration config = null)
        {
            config = config ?? _defaultConfig;

            try
            {
                if (string.IsNullOrEmpty(code))
                    return (OptimizationAssessment.NotRequired, "分析失败: 输入代码为空", CreateErrorPrompt("输入代码为空"));

                var result = PerformAnalysis(code, config);
                if (!string.IsNullOrEmpty(result.Error))
                    return (OptimizationAssessment.NotRequired, $"分析失败: {result.Error}", CreateErrorPrompt(result.Error));

                var suggestions = new RefactoringSuggestionSkeleton(config).GenerateSuggestions(result.Elements, result.Clusters, config);
                var assessment = DetermineOptimizationAssessment(suggestions, result);
                string report = new AnalysisReportSkeleton().GenerateReport(result, suggestions, assessment);
                RecommendedPrompt recommendedPrompt;

                if (assessment != OptimizationAssessment.NotRequired)
                {
                    recommendedPrompt = new PromptRecommenderSkeleton(config).RecommendForCode(code, config);
                    report = new AnalysisReportSkeleton().EnhanceReportWithPrompt(report, recommendedPrompt, assessment);
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

        public async Task<(OptimizationAssessment Assessment, string Report, RecommendedPrompt Prompt)> AnalyzeWithRecommendationAsync(
            string code,
            AnalysisConfiguration config = null,
            IProgress<AnalysisProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            config = config ?? _defaultConfig;

            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                progress?.Report(new AnalysisProgress(0, 5, "开始分析"));

                if (string.IsNullOrEmpty(code))
                    return (OptimizationAssessment.NotRequired, "分析失败: 输入代码为空", CreateErrorPrompt("输入代码为空"));

                progress?.Report(new AnalysisProgress(1, 5, "执行代码分析"));
                var result = PerformAnalysis(code, config);
                if (!string.IsNullOrEmpty(result.Error))
                    return (OptimizationAssessment.NotRequired, $"分析失败: {result.Error}", CreateErrorPrompt(result.Error));

                progress?.Report(new AnalysisProgress(2, 5, "生成重构建议"));
                var suggestions = new RefactoringSuggestionSkeleton(config).GenerateSuggestions(result.Elements, result.Clusters, config);

                progress?.Report(new AnalysisProgress(3, 5, "计算优化评估"));
                var assessment = DetermineOptimizationAssessment(suggestions, result);

                progress?.Report(new AnalysisProgress(4, 5, "生成报告"));
                string report = new AnalysisReportSkeleton().GenerateReport(result, suggestions, assessment);
                RecommendedPrompt recommendedPrompt;

                if (assessment != OptimizationAssessment.NotRequired)
                {
                    recommendedPrompt = new PromptRecommenderSkeleton(config).RecommendForCode(code, config);
                    report = new AnalysisReportSkeleton().EnhanceReportWithPrompt(report, recommendedPrompt, assessment);
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

                progress?.Report(new AnalysisProgress(5, 5, "分析完成"));
                return (assessment, report, recommendedPrompt);
            }, cancellationToken);
        }

        public List<RefactoringSuggestion> GetSuggestions(string code, AnalysisConfiguration config = null)
        {
            config = config ?? _defaultConfig;
            if (string.IsNullOrEmpty(code)) return new List<RefactoringSuggestion>();

            var result = PerformAnalysis(code, config);
            return new RefactoringSuggestionSkeleton(config).GenerateSuggestions(result.Elements, result.Clusters, config);
        }

        private static AnalysisResult PerformAnalysis(string code, AnalysisConfiguration config)
        {
            var result = new AnalysisResult();
            var sw = System.Diagnostics.Stopwatch.StartNew();

            try
            {
                var syntaxTree = CSharpSyntaxTree.ParseText(code);
                var root = syntaxTree.GetRoot();
                var compilation = CSharpCompilation.Create("OptimizationAnalysis")
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

        public static OptimizationAssessment DetermineOptimizationAssessment(
            List<RefactoringSuggestion> suggestions,
            AnalysisResult result)
        {
            if (suggestions == null || suggestions.Count == 0)
                return OptimizationAssessment.NotRequired;

            double totalScore = 0;

            double suggestionScore = suggestions.Sum(s => s.Confidence);
            totalScore += suggestionScore * 0.4;

            if (result?.Elements != null)
            {
                var highComplexityCount = result.Elements.Count(e =>
                    e.Type == ElementType.Method && e.ComplexityScore > CodeAnalysisConstants.HIGH_COMPLEXITY_THRESHOLD);
                totalScore += highComplexityCount * 0.3;
            }

            if (result?.Clusters != null && result.Clusters.Count > 0)
            {
                var lowCohesionClusters = result.Clusters.Count(c => c.Cohesion < CodeAnalysisConstants.LOW_COHESION_THRESHOLD);
                totalScore += lowCohesionClusters * 0.3;
            }

            if (totalScore >= CodeAnalysisConstants.ASSESSMENT_HIGH_THRESHOLD)
                return OptimizationAssessment.HighPriority;
            if (totalScore >= CodeAnalysisConstants.ASSESSMENT_MEDIUM_THRESHOLD)
                return OptimizationAssessment.MediumPriority;
            if (totalScore >= CodeAnalysisConstants.ASSESSMENT_LOW_THRESHOLD)
                return OptimizationAssessment.LowPriority;

            return OptimizationAssessment.NotRequired;
        }

        public static string GetAssessmentDisplay(OptimizationAssessment assessment)
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

        private static RecommendedPrompt CreateErrorPrompt(string error) => new RecommendedPrompt
        {
            Prompt = "系统分析过程中出现错误，请检查代码格式或重试。",
            Confidence = 0.0,
            Reason = $"分析异常: {error}",
            AnalysisDetails = new Dictionary<string, object> { ["Exception"] = error }
        };
    }
}
