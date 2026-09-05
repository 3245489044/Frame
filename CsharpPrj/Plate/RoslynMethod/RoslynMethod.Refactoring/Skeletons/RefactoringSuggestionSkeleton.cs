using RoslynSpace.RoslynSyntaxUtilities.Base;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace RoslynSpace.RoslynSyntaxUtilities.Base
{
    public class RefactoringSuggestionSkeleton
    {
        private readonly AnalysisConfiguration _defaultConfig;

        public RefactoringSuggestionSkeleton(AnalysisConfiguration config = null)
        {
            _defaultConfig = config ?? new AnalysisConfiguration
            {
                ConfidenceThreshold = CodeAnalysisConstants.CONFIDENCE_MEDIUM,
                DistanceThreshold = 5.0,
                MaxClusters = 8
            };
        }

        public List<RefactoringSuggestion> GenerateSuggestions(
            List<EnhancedCodeElement> elements,
            List<CodeCluster> clusters,
            AnalysisConfiguration config = null)
        {
            config = config ?? _defaultConfig;
            var suggestions = new List<RefactoringSuggestion>();

            if (elements == null || elements.Count == 0)
                return suggestions;

            suggestions.AddRange(GenerateClassExtractionSuggestions(clusters, config));

            suggestions.AddRange(GenerateMethodOptimizationSuggestions(elements, config));

            suggestions.AddRange(GenerateVariableOptimizationSuggestions(elements, config));

            suggestions.AddRange(GenerateParameterObjectSuggestions(elements, config));

            return suggestions;
        }

        public async Task<List<RefactoringSuggestion>> GenerateSuggestionsAsync(
            List<EnhancedCodeElement> elements,
            List<CodeCluster> clusters,
            AnalysisConfiguration config = null,
            CancellationToken cancellationToken = default)
        {
            return await Task.Run(() =>
            {
                cancellationToken.ThrowIfCancellationRequested();
                return GenerateSuggestions(elements, clusters, config);
            }, cancellationToken);
        }

        private List<RefactoringSuggestion> GenerateClassExtractionSuggestions(
            List<CodeCluster> clusters, AnalysisConfiguration config)
        {
            var suggestions = new List<RefactoringSuggestion>();

            if (clusters == null || clusters.Count == 0)
                return suggestions;

            foreach (var cluster in clusters)
            {
                if (cluster.Elements.Count < CodeAnalysisConstants.MIN_CLUSTER_ELEMENTS)
                    continue;

                if (cluster.Cohesion < CodeAnalysisConstants.LOW_COHESION_THRESHOLD)
                    continue;

                var suggestion = new RefactoringSuggestion(
                    $"EXTRACT_CLASS_{cluster.Id}",
                    RefactoringType.ExtractClass)
                {
                    Confidence = CalculateClusterConfidence(cluster),
                    Category = RefactoringTypeNames.CLASS_EXTRACTION,
                    TargetLocation = cluster.Elements.FirstOrDefault()?.ContainingType ?? "",
                    ElementNames = cluster.Elements.Select(e => e.Name).ToList(),
                    DetailedDescription = BuildClassExtractionDescription(cluster),
                    Risks = new List<string>
                    {
                        "提取类后可能需要调整依赖关系",
                        "注意保持接口兼容性"
                    }
                };

                suggestions.Add(suggestion);
            }

            return suggestions;
        }

        private List<RefactoringSuggestion> GenerateMethodOptimizationSuggestions(
            List<EnhancedCodeElement> elements, AnalysisConfiguration config)
        {
            var suggestions = new List<RefactoringSuggestion>();

            foreach (var element in elements.Where(e => e.Type == ElementType.Method))
            {
                if (element.ComplexityScore > CodeAnalysisConstants.HIGH_COMPLEXITY_THRESHOLD)
                {
                    suggestions.Add(new RefactoringSuggestion(
                        $"COMPLEXITY_{element.Id}",
                        RefactoringType.ExtractMethod)
                    {
                        Confidence = CalculateComplexityConfidence(element.ComplexityScore),
                        Category = RecommendationCategoryNames.HIGH_COMPLEXITY_METHOD_OPTIMIZATION,
                        TargetLocation = element.ContainingType,
                        ElementNames = new List<string> { element.Name },
                        DetailedDescription = BuildComplexityDescription(element),
                        Risks = new List<string>
                        {
                            "提取方法后可能增加调用开销",
                            "注意保持方法语义完整性"
                        }
                    });
                }

                if (element.BodyLines > CodeAnalysisConstants.LONG_METHOD_LINES_THRESHOLD)
                {
                    suggestions.Add(new RefactoringSuggestion(
                        $"LONG_METHOD_{element.Id}",
                        RefactoringType.ExtractMethod)
                    {
                        Confidence = CalculateLongMethodConfidence(element.BodyLines),
                        Category = RecommendationCategoryNames.METHOD_EXTRACTION_OPTIMIZATION,
                        TargetLocation = element.ContainingType,
                        ElementNames = new List<string> { element.Name },
                        DetailedDescription = BuildLongMethodDescription(element),
                        Risks = new List<string>
                        {
                            "提取方法后可能增加调用开销",
                            "注意保持方法语义完整性"
                        }
                    });
                }

                if (element.ParameterCount > CodeAnalysisConstants.TOO_MANY_PARAMETERS_THRESHOLD)
                {
                    suggestions.Add(new RefactoringSuggestion(
                        $"PARAMETERS_{element.Id}",
                        RefactoringType.IntroduceParameterObject)
                    {
                        Confidence = CodeAnalysisConstants.CONFIDENCE_HIGH,
                        Category = RecommendationCategoryNames.PARAMETER_OPTIMIZATION,
                        TargetLocation = element.ContainingType,
                        ElementNames = new List<string> { element.Name },
                        DetailedDescription = BuildParameterDescription(element),
                        Risks = new List<string>
                        {
                            "引入参数对象后需要更新所有调用点",
                            "注意保持向后兼容性"
                        }
                    });
                }
            }

            return suggestions;
        }

        private List<RefactoringSuggestion> GenerateVariableOptimizationSuggestions(
            List<EnhancedCodeElement> elements, AnalysisConfiguration config)
        {
            var suggestions = new List<RefactoringSuggestion>();

            foreach (var element in elements.Where(e => e.Type == ElementType.Method))
            {
                if (element.VariableReuseRatio < CodeAnalysisConstants.LOW_VARIABLE_REUSE_THRESHOLD
                    && element.Variables.Count > CodeAnalysisConstants.MANY_LOCAL_VARIABLES_THRESHOLD)
                {
                    suggestions.Add(new RefactoringSuggestion(
                        $"VARIABLE_REUSE_{element.Id}",
                        RefactoringType.ExtractMethod)
                    {
                        Confidence = CodeAnalysisConstants.CONFIDENCE_MEDIUM,
                        Category = RecommendationCategoryNames.VARIABLE_REUSE_OPTIMIZATION,
                        TargetLocation = element.ContainingType,
                        ElementNames = new List<string> { element.Name },
                        DetailedDescription = BuildVariableReuseDescription(element),
                        VariableDetails = element.Variables
                            .Select(v => new VariableOptimizationDetail
                            {
                                VariableName = v.Name,
                                UsageCount = v.UsageCount,
                                IsParameter = v.IsParameter,
                                Type = v.Type
                            }).ToList(),
                        Risks = new List<string>
                        {
                            "变量重用优化可能影响代码可读性",
                            "注意保持变量作用域清晰"
                        }
                    });
                }

                if (element.Variables.Count > CodeAnalysisConstants.MANY_LOCAL_VARIABLES_THRESHOLD)
                {
                    suggestions.Add(new RefactoringSuggestion(
                        $"TOO_MANY_VARS_{element.Id}",
                        RefactoringType.ExtractMethod)
                    {
                        Confidence = CodeAnalysisConstants.CONFIDENCE_LOW,
                        Category = RecommendationCategoryNames.TOO_MANY_LOCAL_VARIABLES_OPTIMIZATION,
                        TargetLocation = element.ContainingType,
                        ElementNames = new List<string> { element.Name },
                        DetailedDescription = BuildTooManyVariablesDescription(element),
                        VariableDetails = element.Variables
                            .Select(v => new VariableOptimizationDetail
                            {
                                VariableName = v.Name,
                                UsageCount = v.UsageCount,
                                IsParameter = v.IsParameter,
                                Type = v.Type
                            }).ToList(),
                        Risks = new List<string>
                        {
                            "提取方法后可能增加调用开销",
                            "注意保持变量作用域清晰"
                        }
                    });
                }
            }

            return suggestions;
        }

        private List<RefactoringSuggestion> GenerateParameterObjectSuggestions(
            List<EnhancedCodeElement> elements, AnalysisConfiguration config)
        {
            var suggestions = new List<RefactoringSuggestion>();

            foreach (var element in elements.Where(e => e.Type == ElementType.Method))
            {
                if (element.ParameterCount > CodeAnalysisConstants.TOO_MANY_PARAMETERS_THRESHOLD)
                {
                    suggestions.Add(new RefactoringSuggestion(
                        $"PARAM_OBJECT_{element.Id}",
                        RefactoringType.IntroduceParameterObject)
                    {
                        Confidence = CodeAnalysisConstants.CONFIDENCE_HIGH,
                        Category = RefactoringTypeNames.PARAMETER_OBJECT_EXTRACTION,
                        TargetLocation = element.ContainingType,
                        ElementNames = new List<string> { element.Name },
                        DetailedDescription = BuildParameterObjectDescription(element),
                        Risks = new List<string>
                        {
                            "引入参数对象后需要更新所有调用点",
                            "注意保持向后兼容性"
                        }
                    });
                }
            }

            return suggestions;
        }

        private static double CalculateClusterConfidence(CodeCluster cluster)
        {
            double confidence = CodeAnalysisConstants.CONFIDENCE_MEDIUM;
            if (cluster.Cohesion > CodeAnalysisConstants.GOOD_COHESION_THRESHOLD)
                confidence += 0.2;
            if (cluster.Elements.Count >= CodeAnalysisConstants.MIN_CLUSTER_ELEMENTS * 2)
                confidence += 0.1;
            return Math.Min(confidence, CodeAnalysisConstants.CONFIDENCE_MAX);
        }

        private static double CalculateComplexityConfidence(int complexity)
        {
            if (complexity > CodeAnalysisConstants.VERY_HIGH_COMPLEXITY_THRESHOLD)
                return CodeAnalysisConstants.CONFIDENCE_VERY_HIGH;
            if (complexity > CodeAnalysisConstants.HIGH_COMPLEXITY_THRESHOLD)
                return CodeAnalysisConstants.CONFIDENCE_HIGH;
            return CodeAnalysisConstants.CONFIDENCE_MEDIUM;
        }

        private static double CalculateLongMethodConfidence(double bodyLines)
        {
            if (bodyLines > CodeAnalysisConstants.VERY_LONG_METHOD_LINES_THRESHOLD)
                return CodeAnalysisConstants.CONFIDENCE_VERY_HIGH;
            if (bodyLines > CodeAnalysisConstants.LONG_METHOD_LINES_THRESHOLD)
                return CodeAnalysisConstants.CONFIDENCE_HIGH;
            return CodeAnalysisConstants.CONFIDENCE_MEDIUM;
        }

        private static string BuildClassExtractionDescription(CodeCluster cluster)
        {
            var elementNames = string.Join("、", cluster.Elements.Select(e => e.Name));
            return $"建议将类中的 {elementNames} 等 {cluster.Elements.Count} 个元素提取到新类中。" +
                   $"当前聚类内聚度: {cluster.Cohesion:F2}，耦合度: {cluster.Coupling:F2}。";
        }

        private static string BuildComplexityDescription(EnhancedCodeElement element)
        {
            return $"方法 '{element.Name}' 的复杂度为 {element.ComplexityScore}，" +
                   $"超过高复杂度阈值 {CodeAnalysisConstants.HIGH_COMPLEXITY_THRESHOLD}。" +
                   "建议将复杂逻辑拆分为多个小方法。";
        }

        private static string BuildLongMethodDescription(EnhancedCodeElement element)
        {
            return $"方法 '{element.Name}' 的代码行数为 {element.BodyLines}，" +
                   $"超过长方法阈值 {CodeAnalysisConstants.LONG_METHOD_LINES_THRESHOLD}。" +
                   "建议将部分逻辑提取为独立方法。";
        }

        private static string BuildParameterDescription(EnhancedCodeElement element)
        {
            return $"方法 '{element.Name}' 有 {element.ParameterCount} 个参数，" +
                   $"超过参数阈值 {CodeAnalysisConstants.TOO_MANY_PARAMETERS_THRESHOLD}。" +
                   "建议减少参数数量或引入参数对象。";
        }

        private static string BuildVariableReuseDescription(EnhancedCodeElement element)
        {
            return $"方法 '{element.Name}' 的变量重用率为 {element.VariableReuseRatio:F2}，" +
                   $"低于低重用率阈值 {CodeAnalysisConstants.LOW_VARIABLE_REUSE_THRESHOLD}。" +
                   "建议优化变量使用方式，提高重用率。";
        }

        private static string BuildTooManyVariablesDescription(EnhancedCodeElement element)
        {
            return $"方法 '{element.Name}' 有 {element.Variables.Count} 个局部变量，" +
                   $"超过局部变量阈值 {CodeAnalysisConstants.MANY_LOCAL_VARIABLES_THRESHOLD}。" +
                   "建议将部分逻辑提取为独立方法以减少局部变量数量。";
        }

        private static string BuildParameterObjectDescription(EnhancedCodeElement element)
        {
            return $"方法 '{element.Name}' 有 {element.ParameterCount} 个参数，" +
                   $"超过参数阈值 {CodeAnalysisConstants.TOO_MANY_PARAMETERS_THRESHOLD}。" +
                   "建议将多个参数封装为参数对象。";
        }
    }
}
