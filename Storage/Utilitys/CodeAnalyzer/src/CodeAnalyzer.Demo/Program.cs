using CodeAnalyzer.Analysis.Services;
using CodeAnalyzer.Analysis.Skeletons;
using CodeAnalyzer.Core.Models;
using CodeAnalyzer.Prompt.Constants;
using CodeAnalyzer.Prompt.Services;
using CodeAnalyzer.Prompt.Skeletons;

namespace CodeAnalyzer.Demo;

public static class Program
{
    public static void Main()
    {
        Console.WriteLine(PromptRecommenderConstants.TestMessages.DemoHeader);
        Console.WriteLine();

        string sampleCode = @"
using System;
using System.Collections.Generic;

namespace Demo
{
    public class Calculator
    {
        private int _result;

        public int Add(int a, int b)
        {
            _result = a + b;
            return _result;
        }

        public int Subtract(int a, int b)
        {
            _result = a - b;
            return _result;
        }

        public int Multiply(int a, int b)
        {
            _result = a * b;
            return _result;
        }

        public int Divide(int a, int b)
        {
            if (b == 0)
                throw new ArgumentException(""除数不能为零"");
            _result = a / b;
            return _result;
        }

        public int ComplexMethod(int x, int y, int z, int w, int v)
        {
            int temp1 = x + y;
            int temp2 = y + z;
            int temp3 = z + w;
            int temp4 = w + v;
            int temp5 = v + x;
            int result = 0;

            if (temp1 > 10)
            {
                if (temp2 > 20)
                {
                    for (int i = 0; i < temp1; i++)
                    {
                        result += i;
                    }
                }
            }

            while (result < 100)
            {
                result += temp3;
                if (result > 50) break;
            }

            return result;
        }
    }
}";

        Console.WriteLine(PromptRecommenderConstants.TestMessages.CodeLabel);
        Console.WriteLine(sampleCode);
        Console.WriteLine();

        var config = new AnalysisConfiguration();

        Console.WriteLine(PromptRecommenderConstants.TestMessages.AnalysisStep1);
        var result = CodeAnalysisEngine.Analyze(sampleCode, config);
        Console.WriteLine(string.Format(PromptRecommenderConstants.TestMessages.AnalysisTimeLabel, result.AnalysisTime));
        Console.WriteLine(string.Format(PromptRecommenderConstants.TestMessages.AnalysisErrorLabel, string.IsNullOrEmpty(result.Error) ? PromptRecommenderConstants.TestMessages.NoError : result.Error));
        Console.WriteLine();

        Console.WriteLine(string.Format(PromptRecommenderConstants.TestMessages.Step2, PromptRecommenderConstants.DisplayConstants.MaxElementsPreview));
        foreach (var element in result.Elements.Take(PromptRecommenderConstants.DisplayConstants.MaxElementsPreview))
        {
            Console.WriteLine($"  [{element.Type}] {element.Name} (行 {element.LineNumber})");
        }
        Console.WriteLine();

        Console.WriteLine(string.Format(PromptRecommenderConstants.TestMessages.Step3, PromptRecommenderConstants.DisplayConstants.MaxDistancesPreview));
        foreach (var dist in result.Distances.Take(PromptRecommenderConstants.DisplayConstants.MaxDistancesPreview))
        {
            var src = result.Elements.FirstOrDefault(e => e.Id == dist.SourceId);
            var tgt = result.Elements.FirstOrDefault(e => e.Id == dist.TargetId);
            Console.WriteLine($"  {src?.Name} -> {tgt?.Name}: 距离={dist.Distance:F2}, 相似度={dist.Similarity:F2}");
        }
        Console.WriteLine();

        Console.WriteLine(PromptRecommenderConstants.TestMessages.Step4);
        foreach (var cluster in result.Clusters)
        {
            Console.WriteLine(string.Format(PromptRecommenderConstants.TestMessages.ClusterFormat,
                cluster.Id, cluster.Name, cluster.Cohesion, cluster.Coupling, cluster.Elements.Count));
            foreach (var element in cluster.Elements.Take(PromptRecommenderConstants.DisplayConstants.MaxElementNamesPreview))
            {
                Console.WriteLine(string.Format(PromptRecommenderConstants.TestMessages.ElementSampleFormat, element.Name, element.Type == ElementType.Method ? "()" : ""));
            }
            if (cluster.Elements.Count > PromptRecommenderConstants.DisplayConstants.MaxElementNamesPreview)
                Console.WriteLine(PromptRecommenderConstants.TestMessages.Ellipsis);
        }
        Console.WriteLine();

        Console.WriteLine(PromptRecommenderConstants.TestMessages.Step5);
        var suggestions = RefactoringSuggestionGenerator.GenerateSuggestions(result.Elements, result.Clusters, config);
        if (suggestions.Any())
        {
            foreach (var s in suggestions)
            {
                Console.WriteLine($"  [{s.Category}] {s.DetailedDescription} (置信度: {s.Confidence:P1})");
            }
        }
        else
        {
            Console.WriteLine($"  {PromptRecommenderConstants.TestMessages.NoSuggestions}");
        }
        Console.WriteLine();

        Console.WriteLine(PromptRecommenderConstants.TestMessages.Step6);
        var assessment = OptimizationAssessmentCalculator.DetermineOptimizationAssessment(suggestions, result);
        Console.WriteLine($"  评估等级: {OptimizationAssessmentCalculator.GetAssessmentDisplay(assessment)}");
        Console.WriteLine($"  评估说明: {OptimizationAssessmentCalculator.GetAssessmentDescription(assessment)}");
        Console.WriteLine();

        Console.WriteLine(PromptRecommenderConstants.TestMessages.Step7);
        var metrics = CodeQualityMetricsCalculator.CalculateEnhancedMetrics(result.Elements, result.Clusters, suggestions);
        foreach (var kv in metrics.Take(8))
        {
            Console.WriteLine($"  {kv.Key}: {kv.Value}");
        }
        Console.WriteLine();

        Console.WriteLine(PromptRecommenderConstants.TestMessages.Step8);
        var recommender = new PromptRecommender(config);
        var prompt = recommender.RecommendForCode(sampleCode);
        Console.WriteLine($"  推荐提示: {prompt.Prompt}");
        Console.WriteLine($"  置信度: {prompt.Confidence:P1}");
        Console.WriteLine($"  理由: {prompt.Reason}");
        Console.WriteLine();

        Console.WriteLine(PromptRecommenderConstants.TestMessages.Step9);
        var (assessment2, report, _) = EnhancedOptimizationAnalyzer.AnalyzeWithRecommendation(sampleCode, config);
        Console.WriteLine($"  评估等级: {OptimizationAssessmentCalculator.GetAssessmentDisplay(assessment2)}");
        Console.WriteLine($"  报告预览 ({PromptRecommenderConstants.DisplayConstants.ReportPreviewLength} 字符):");
        Console.WriteLine(report.Length > PromptRecommenderConstants.DisplayConstants.ReportPreviewLength
            ? report[..PromptRecommenderConstants.DisplayConstants.ReportPreviewLength] + PromptRecommenderConstants.TestMessages.AndMore
            : report);
        Console.WriteLine();

        Console.WriteLine(PromptRecommenderConstants.TestMessages.Step10);
        var quickMetrics = PromptQualityAnalyzer.GetQuickMetrics(sampleCode, config);
        foreach (var kv in quickMetrics)
        {
            Console.WriteLine($"  {kv.Key}: {kv.Value}");
        }
        Console.WriteLine();

        Console.WriteLine(PromptRecommenderConstants.TestMessages.DemoEnd);
    }
}
