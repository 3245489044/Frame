#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using UniversalLayoutFramework;

namespace UniversalLayoutMath
{

    public class OptimizationResult
    {
        public List<ElementOutput> FinalElements { get; set; } = new();
        public List<OverlapReport> History { get; set; } = new();
        public int IterationsUsed { get; set; }
        public bool Converged { get; set; }
        public OverlapReport? InitialReport { get; set; }
        public OverlapReport? FinalReport { get; set; }
        public string Summary
        {
            get
            {
                if (InitialReport == null || !InitialReport.HasOverlap)
                    return "无需优化，无重叠";

                string status = Converged ? "✅ 已收敛" : "⚠️ 未完全收敛";
                return $"{status}: {IterationsUsed} 次迭代, " +
                       $"初始重叠 {InitialReport.Overlaps.Count} 处/{InitialReport.OverlappingElementCount} 元素, " +
                       $"最终重叠 {FinalReport?.Overlaps.Count ?? 0} 处/{FinalReport?.OverlappingElementCount ?? 0} 元素, " +
                       $"最大重叠率 {InitialReport.MaxOverlapRatio:P1} → {FinalReport?.MaxOverlapRatio:P1}";
            }
        }
    }


    public class AutoLayoutOptimizer
    {
        public int MaxIterations { get; set; } = 20;
        public double ConvergenceThreshold { get; set; } = 0.01;
        public bool LogEnabled { get; set; } = false;
        public int MaxNoImprovementCount { get; set; } = 3;

        private Action<string>? _logger;

        public AutoLayoutOptimizer(Action<string>? logger = null)
        {
            _logger = logger;
        }

        public OptimizationResult Optimize(List<ElementOutput> elements, int parentWidth, int parentHeight)
        {
            var result = new OptimizationResult
            {
                FinalElements = new List<ElementOutput>(elements),
                History = new List<OverlapReport>()
            };

            if (elements.Count < 2)
            {
                result.Converged = true;
                return result;
            }

            result.InitialReport = DetectOverlapsInOutput(
                result.FinalElements, parentWidth, parentHeight);
            result.History.Add(result.InitialReport);

            if (!result.InitialReport.HasOverlap)
            {
                result.Converged = true;
                result.FinalReport = result.InitialReport;
                Log("无需优化，初始无重叠");
                return result;
            }

            Log($"开始优化: {result.InitialReport.GetSummary()}");

            int noImprovementCount = 0;
            double previousMaxRatio = result.InitialReport.MaxOverlapRatio;

            for (int iter = 0; iter < MaxIterations; iter++)
            {
                var currentReport = result.History.Last();

                if (!currentReport.HasOverlap || currentReport.MaxOverlapRatio < ConvergenceThreshold)
                {
                    result.Converged = true;
                    Log($"第 {iter + 1} 次迭代后收敛");
                    break;
                }

                if (currentReport.MaxOverlapRatio >= previousMaxRatio)
                {
                    noImprovementCount++;
                    Log($"  无改善检测: 第 {noImprovementCount}/{MaxNoImprovementCount} 次 (当前={currentReport.MaxOverlapRatio:F4}, 上次={previousMaxRatio:F4})");
                    if (noImprovementCount >= MaxNoImprovementCount)
                    {
                        Log($"  连续 {MaxNoImprovementCount} 次迭代无改善，提前终止");
                        break;
                    }
                }
                else
                {
                    noImprovementCount = 0;
                    previousMaxRatio = currentReport.MaxOverlapRatio;
                }

                Log($"第 {iter + 1} 次迭代: {currentReport.GetSummary()}");

                var elementDescs = result.FinalElements
                    .Select(e => new OverlapMath.ElementDesc
                    {
                        Name = e.ControlName,
                        Rect = new Rect(e.X, e.Y, e.Width, e.Height)
                    })
                    .ToList();
                var zOrderMap = result.FinalElements
                    .ToDictionary(e => e.ControlName, e => e.ZOrder);
                var graph = ConflictGraph.BuildFromReport(currentReport, elementDescs, zOrderMap);
                Log($"  {graph.GetSummary()}");

                var priorityOrder = graph.GetPriorityOrder();
                var optimizer = new MultiObjectiveOptimizer(parentWidth, parentHeight);
                int fixesApplied = 0;

                foreach (var elementName in priorityOrder)
                {
                    var relatedEdges = graph.GetEdgesForElement(elementName);
                    if (relatedEdges.Count == 0) continue;

                    var relatedOverlaps = currentReport.Overlaps
                        .Where(o => o.ElementA == elementName || o.ElementB == elementName)
                        .OrderByDescending(o => o.OverlapRatio)
                        .ToList();

                    foreach (var overlap in relatedOverlaps)
                    {
                        OverlapPair adjustedOverlap = overlap;
                        if (overlap.ElementB != elementName)
                        {
                            adjustedOverlap = new OverlapPair
                            {
                                ElementA = overlap.ElementB,
                                ElementB = overlap.ElementA,
                                RectA = overlap.RectB,
                                RectB = overlap.RectA,
                                OverlapRect = overlap.OverlapRect,
                                OverlapArea = overlap.OverlapArea,
                                OverlapRatio = overlap.OverlapRatio,
                                Level = overlap.Level
                            };
                        }

                        var bestFix = optimizer.SelectBestStrategy(adjustedOverlap, elementDescs);
                        if (bestFix == null || !bestFix.IsValid) continue;

                        ApplyFix(result.FinalElements, elementName, bestFix);
                        fixesApplied++;
                        Log($"  修复: '{elementName}' {bestFix.Description} (代价={bestFix.Cost:F3})");
                    }
                }

                if (fixesApplied == 0)
                {
                    Log("  无法应用更多修复，停止迭代");
                    break;
                }

                var newReport = DetectOverlapsInOutput(
                    result.FinalElements, parentWidth, parentHeight);
                result.History.Add(newReport);
            }

            result.IterationsUsed = result.History.Count;
            result.FinalReport = result.History.Last();
            result.Converged = !result.FinalReport.HasOverlap ||
                               result.FinalReport.MaxOverlapRatio < ConvergenceThreshold;

            Log($"优化完成: {result.Summary}");

            return result;
        }

        private void ApplyFix(List<ElementOutput> elements, string elementName, FixScore fix)
        {
            for (int i = 0; i < elements.Count; i++)
            {
                if (elements[i].ControlName == elementName)
                {
                    elements[i] = new ElementOutput
                    {
                        ControlName = elementName,
                        X = fix.SuggestedX,
                        Y = fix.SuggestedY,
                        Width = fix.SuggestedWidth,
                        Height = fix.SuggestedHeight,
                        ZOrder = elements[i].ZOrder
                    };
                    break;
                }
            }
        }

        private static OverlapReport DetectOverlapsInOutput(
            List<ElementOutput> elements, int parentWidth, int parentHeight)
        {
            var descs = elements.Select(e => new OverlapMath.ElementDesc
            {
                Name = e.ControlName,
                Rect = new Rect(e.X, e.Y, e.Width, e.Height)
            }).ToList();
            return OverlapMath.DetectOverlapsInElements(descs);
        }

        private void Log(string message)
        {
            if (LogEnabled)
                _logger?.Invoke($"[AutoLayoutOptimizer] {message}");
        }
    }


    public static class LayoutOptimizationExtensions
    {
        public static OverlapReport DetectOverlaps(this List<ElementOutput> elements, int parentWidth, int parentHeight)
        {
            if (elements != null && elements.Count > 0)
            {
                return DetectOverlapsInOutput(elements, parentWidth, parentHeight);
            }
            return new OverlapReport();
        }

        public static OptimizationResult OptimizeLayout(
            this List<ElementOutput> elements, int parentWidth, int parentHeight,
            Action<string>? logger = null)
        {
            if (elements != null && elements.Count > 0)
            {
                var optimizer = new AutoLayoutOptimizer(logger);
                return optimizer.Optimize(elements, parentWidth, parentHeight);
            }
            return new OptimizationResult
            {
                FinalElements = elements ?? new List<ElementOutput>(),
                Converged = true
            };
        }

        private static OverlapReport DetectOverlapsInOutput(
            List<ElementOutput> elements, int parentWidth, int parentHeight)
        {
            var descs = elements.Select(e => new OverlapMath.ElementDesc
            {
                Name = e.ControlName,
                Rect = new Rect(e.X, e.Y, e.Width, e.Height)
            }).ToList();
            return OverlapMath.DetectOverlapsInElements(descs);
        }
    }
}
