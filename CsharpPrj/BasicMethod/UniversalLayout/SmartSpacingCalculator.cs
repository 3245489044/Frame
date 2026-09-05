#nullable enable
using System;
using System.Collections.Generic;

namespace UniversalLayoutMath
{

    public static class SmartSpacingCalculator
    {

        public static int CalculateBaseSpacing(int parentWidth, int parentHeight,
            int baseSpacing = 5, int minSpacing = 3, int maxSpacing = 20)
        {
            if (parentWidth <= 0 || parentHeight <= 0)
                return baseSpacing;

            double diagonal = Math.Sqrt(
                (double)parentWidth * parentWidth +
                (double)parentHeight * parentHeight);
            const double referenceDiagonal = 2203.0;
            double ratio = diagonal / referenceDiagonal;
            int spacing = (int)Math.Round(baseSpacing * ratio);
            return spacing < minSpacing ? minSpacing : (spacing > maxSpacing ? maxSpacing : spacing);
        }

        public static int CalculateHeaderHeight(int parentHeight,
            int baseHeight = 25, int minHeight = 18, int maxHeight = 40)
        {
            if (parentHeight <= 0)
                return baseHeight;

            double ratio = (double)parentHeight / 720.0;
            int height = (int)Math.Round(baseHeight * ratio);
            return height < minHeight ? minHeight : (height > maxHeight ? maxHeight : height);
        }


        public static int CalculateMinElementSize(int parentDimension,
            double minRatio = 0.02, int absoluteMin = 10)
        {
            if (parentDimension <= 0)
                return absoluteMin;

            return Math.Max((int)Math.Round(parentDimension * minRatio), absoluteMin);
        }


        private static readonly Dictionary<string, double> _typeFactors = new()
        {
            { "label", 0.8 },
            { "checkbox", 1.0 },
            { "button", 1.5 },
            { "textbox", 1.2 },
            { "groupbox", 2.0 },
            { "combobox", 1.2 },
            { "panel", 1.5 },
            { "listbox", 1.2 },
            { "datagridview", 1.5 },
            { "picturebox", 1.0 },
            { "progressbar", 1.0 },
            { "trackbar", 1.2 },
        };

        public static double GetTypeFactor(string controlType)
        {
            if (string.IsNullOrWhiteSpace(controlType))
                return 1.0;

            return _typeFactors.TryGetValue(
                controlType.ToLowerInvariant(), out var f) ? f : 1.0;
        }

        public static int CalculateSpacing(string controlType, int parentWidth, int parentHeight)
        {
            double baseSpacing = CalculateBaseSpacing(parentWidth, parentHeight);
            double factor = GetTypeFactor(controlType);
            return (int)Math.Round(baseSpacing * factor);
        }

        public static int CalculateSpacing(int parentWidth, int parentHeight)
        {
            return CalculateBaseSpacing(parentWidth, parentHeight);
        }


        public static int CalculateMaxIterations(OverlapReport? initialReport,
            int baseIterations = 10, int maxIterations = 30)
        {
            if (initialReport == null || !initialReport.HasOverlap)
                return 0;

            double severityFactor = initialReport.WorstLevel switch
            {
                OverlapLevel.Slight => 0.5,
                OverlapLevel.Moderate => 1.0,
                OverlapLevel.Severe => 1.5,
                OverlapLevel.Complete => 2.0,
                _ => 1.0
            };

            int countBonus = Math.Min(initialReport.Overlaps.Count / 2, 5);

            int iterations = (int)Math.Round(baseIterations * severityFactor) + countBonus;
            return Math.Min(iterations, maxIterations);
        }

        public static int CalculateMaxIterations(int overlapCount,
            OverlapLevel worstLevel = OverlapLevel.Moderate,
            int baseIterations = 10, int maxIterations = 30)
        {
            if (overlapCount <= 0)
                return 0;

            double severityFactor = worstLevel switch
            {
                OverlapLevel.Slight => 0.5,
                OverlapLevel.Moderate => 1.0,
                OverlapLevel.Severe => 1.5,
                OverlapLevel.Complete => 2.0,
                _ => 1.0
            };

            int countBonus = Math.Min(overlapCount / 2, 5);
            int iterations = (int)Math.Round(baseIterations * severityFactor) + countBonus;
            return Math.Min(iterations, maxIterations);
        }


        public static SpacingParameters CalculateAll(int parentWidth, int parentHeight,
            OverlapReport? initialReport = null)
        {
            return new SpacingParameters
            {
                BaseSpacing = CalculateBaseSpacing(parentWidth, parentHeight),
                HeaderHeight = CalculateHeaderHeight(parentHeight),
                MinElementSize = CalculateMinElementSize(
                    Math.Min(parentWidth, parentHeight)),
                MaxIterations = CalculateMaxIterations(initialReport)
            };
        }
    }

    public class SpacingParameters
    {
        public int BaseSpacing { get; set; } = 5;
        public int HeaderHeight { get; set; } = 25;
        public int MinElementSize { get; set; } = 10;
        public int MaxIterations { get; set; } = 10;

        public override string ToString()
            => $"Spacing={BaseSpacing}, Header={HeaderHeight}, MinSize={MinElementSize}, Iterations={MaxIterations}";
    }
}
