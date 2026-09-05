#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;

namespace UniversalLayoutMath
{

    public static class ProportionalPreflightChecker
    {
        public static PreflightResult PreflightAndFix(
            List<OverlapMath.ProportionalElementDesc> elements,
            int parentWidth, int parentHeight)
        {
            var result = new PreflightResult();

            if (elements == null || elements.Count == 0)
                return result;

            var spacing = SmartSpacingCalculator.CalculateAll(parentWidth, parentHeight);

            var boundsViolations = OverlapMath.DetectBoundsViolations(elements);
            result.BoundsViolations = boundsViolations;

            foreach (var violation in boundsViolations)
            {
                var elem = elements.FirstOrDefault(e => e.Name == violation.ElementName);
                if (elem == null) continue;

                var (l, t, w, h) = ProportionalMath.ClampProportionalPosition(
                    elem.Rect.Left, elem.Rect.Top, elem.Rect.Width, elem.Rect.Height);

                int idx = elements.IndexOf(elem);
                elements[idx] = new OverlapMath.ProportionalElementDesc
                {
                    Name = elem.Name,
                    Rect = new ProportionalRect(l, t, w, h)
                };
            }

            result.OverlapReport = OverlapMath.DetectOverlapsInProportionalElements(elements);

            if (result.OverlapReport.HasOverlap)
            {
                FixOverlapsInProportionalSpace(elements, spacing.BaseSpacing,
                    parentWidth, parentHeight, spacing.MaxIterations);
            }

            result.GapReport = OverlapMath.DetectGapsInProportionalElements(
                elements, parentWidth, parentHeight, spacing.BaseSpacing);

            if (result.GapReport.HasInsufficientGap)
            {
                FixInsufficientGapsInProportionalSpace(elements, spacing.BaseSpacing,
                    parentWidth, parentHeight);
            }

            result.FinalOverlapReport = OverlapMath.DetectOverlapsInProportionalElements(elements);
            result.FinalGapReport = OverlapMath.DetectGapsInProportionalElements(
                elements, parentWidth, parentHeight, spacing.BaseSpacing);
            result.FinalBoundsViolations = OverlapMath.DetectBoundsViolations(elements);

            result.IsFixed = !result.FinalOverlapReport.HasOverlap &&
                             !result.FinalGapReport.HasInsufficientGap &&
                             result.FinalBoundsViolations.Count == 0;

            return result;
        }

        private static void FixOverlapsInProportionalSpace(
            List<OverlapMath.ProportionalElementDesc> elements,
            int baseSpacingPx, int parentWidth, int parentHeight,
            int maxIterations)
        {
            double spacingRatioW = parentWidth > 0 ? (double)baseSpacingPx / parentWidth : 0.005;
            double spacingRatioH = parentHeight > 0 ? (double)baseSpacingPx / parentHeight : 0.005;

            double minSizeRatioW = parentWidth > 0 ? Math.Max(0.02, 10.0 / parentWidth) : 0.02;
            double minSizeRatioH = parentHeight > 0 ? Math.Max(0.02, 10.0 / parentHeight) : 0.02;

            for (int iter = 0; iter < maxIterations; iter++)
            {
                bool anyFixed = false;

                for (int i = 0; i < elements.Count; i++)
                {
                    for (int j = i + 1; j < elements.Count; j++)
                    {
                        var e1 = elements[i];
                        var e2 = elements[j];

                        if (!OverlapMath.WouldOverlapProportional(e1.Rect, e2.Rect))
                            continue;

                        anyFixed = true;

                        double overlapLeft = Math.Max(e1.Rect.Left, e2.Rect.Left);
                        double overlapTop = Math.Max(e1.Rect.Top, e2.Rect.Top);
                        double overlapRight = Math.Min(e1.Rect.Right, e2.Rect.Right);
                        double overlapBottom = Math.Min(e1.Rect.Bottom, e2.Rect.Bottom);

                        double overlapW = overlapRight - overlapLeft;
                        double overlapH = overlapBottom - overlapTop;

                        if (overlapW <= overlapH)
                        {
                            double newLeft = e1.Rect.Right + spacingRatioW;
                            if (newLeft + e2.Rect.Width <= 1.0)
                            {
                                elements[j] = new OverlapMath.ProportionalElementDesc
                                {
                                    Name = e2.Name,
                                    Rect = new ProportionalRect(
                                        newLeft, e2.Rect.Top, e2.Rect.Width, e2.Rect.Height)
                                };
                            }
                            else
                            {
                                double newLeftE1 = e2.Rect.Left - e1.Rect.Width - spacingRatioW;
                                if (newLeftE1 >= 0)
                                {
                                    elements[i] = new OverlapMath.ProportionalElementDesc
                                    {
                                        Name = e1.Name,
                                        Rect = new ProportionalRect(
                                            newLeftE1, e1.Rect.Top, e1.Rect.Width, e1.Rect.Height)
                                    };
                                }
                                else
                                {
                                    double maxWidth = e1.Rect.Left - e2.Rect.Left - spacingRatioW;
                                    if (maxWidth >= minSizeRatioW)
                                    {
                                        elements[j] = new OverlapMath.ProportionalElementDesc
                                        {
                                            Name = e2.Name,
                                            Rect = new ProportionalRect(
                                                e2.Rect.Left, e2.Rect.Top, maxWidth, e2.Rect.Height)
                                        };
                                    }
                                }
                            }
                        }
                        else
                        {
                            double newTop = e1.Rect.Bottom + spacingRatioH;
                            if (newTop + e2.Rect.Height <= 1.0)
                            {
                                elements[j] = new OverlapMath.ProportionalElementDesc
                                {
                                    Name = e2.Name,
                                    Rect = new ProportionalRect(
                                        e2.Rect.Left, newTop, e2.Rect.Width, e2.Rect.Height)
                                };
                            }
                            else
                            {
                                double newTopE1 = e2.Rect.Top - e1.Rect.Height - spacingRatioH;
                                if (newTopE1 >= 0)
                                {
                                    elements[i] = new OverlapMath.ProportionalElementDesc
                                    {
                                        Name = e1.Name,
                                        Rect = new ProportionalRect(
                                            e1.Rect.Left, newTopE1, e1.Rect.Width, e1.Rect.Height)
                                    };
                                }
                                else
                                {
                                    double maxHeight = e1.Rect.Top - e2.Rect.Top - spacingRatioH;
                                    if (maxHeight >= minSizeRatioH)
                                    {
                                        elements[j] = new OverlapMath.ProportionalElementDesc
                                        {
                                            Name = e2.Name,
                                            Rect = new ProportionalRect(
                                                e2.Rect.Left, e2.Rect.Top, e2.Rect.Width, maxHeight)
                                        };
                                    }
                                }
                            }
                        }
                    }
                }

                if (!anyFixed) break;
            }
        }

        private static void FixInsufficientGapsInProportionalSpace(
            List<OverlapMath.ProportionalElementDesc> elements,
            int baseSpacingPx, int parentWidth, int parentHeight)
        {
            double spacingRatioW = parentWidth > 0 ? (double)baseSpacingPx / parentWidth : 0.005;
            double spacingRatioH = parentHeight > 0 ? (double)baseSpacingPx / parentHeight : 0.005;

            for (int i = 0; i < elements.Count; i++)
            {
                for (int j = i + 1; j < elements.Count; j++)
                {
                    var e1 = elements[i];
                    var e2 = elements[j];

                    var gap = OverlapMath.DetectGapProportional(
                        e1.Name, e1.Rect, e2.Name, e2.Rect,
                        parentWidth, parentHeight);

                    if (gap == null || !gap.IsTooClose(baseSpacingPx))
                        continue;

                    if (gap.GapX >= 0 && gap.GapX < baseSpacingPx)
                    {
                        double needed = spacingRatioW - (double)gap.GapX / parentWidth;
                        double newLeft = e2.Rect.Left + needed;
                        if (newLeft + e2.Rect.Width <= 1.0)
                        {
                            elements[j] = new OverlapMath.ProportionalElementDesc
                            {
                                Name = e2.Name,
                                Rect = new ProportionalRect(
                                    newLeft, e2.Rect.Top, e2.Rect.Width, e2.Rect.Height)
                            };
                        }
                    }

                    if (gap.GapY >= 0 && gap.GapY < baseSpacingPx)
                    {
                        double needed = spacingRatioH - (double)gap.GapY / parentHeight;
                        double newTop = e2.Rect.Top + needed;
                        if (newTop + e2.Rect.Height <= 1.0)
                        {
                            elements[j] = new OverlapMath.ProportionalElementDesc
                            {
                                Name = e2.Name,
                                Rect = new ProportionalRect(
                                    e2.Rect.Left, newTop, e2.Rect.Width, e2.Rect.Height)
                            };
                        }
                    }
                }
            }
        }
    }


    public class PreflightResult
    {
        public List<BoundsViolation> BoundsViolations { get; set; } = new();

        public OverlapReport OverlapReport { get; set; } = new();

        public GapReport GapReport { get; set; } = new();

        public OverlapReport FinalOverlapReport { get; set; } = new();

        public GapReport FinalGapReport { get; set; } = new();

        public List<BoundsViolation> FinalBoundsViolations { get; set; } = new();

        public bool IsFixed { get; set; }

        public bool HasIssues =>
            BoundsViolations.Count > 0 ||
            OverlapReport.HasOverlap ||
            GapReport.HasInsufficientGap;

        public string GetSummary()
        {
            var parts = new List<string>();
            if (BoundsViolations.Count > 0)
                parts.Add($"越界: {BoundsViolations.Count}处");
            if (OverlapReport.HasOverlap)
                parts.Add($"重叠: {OverlapReport.Overlaps.Count}处");
            if (GapReport.HasInsufficientGap)
                parts.Add($"间距不足: {GapReport.Gaps.Count}处");

            string status = IsFixed ? "✅ 已修复" : "⚠️ 未完全修复";
            return parts.Count > 0
                ? $"{status}: {string.Join(", ", parts)}"
                : "✅ 无任何问题";
        }
    }
}
