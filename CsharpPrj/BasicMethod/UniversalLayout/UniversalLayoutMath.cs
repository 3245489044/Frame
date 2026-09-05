#nullable enable
using System;
using System.Collections.Generic;

namespace UniversalLayoutMath
{
    public static class UniversalLayoutMath
    {

        public const string Version = "1.0.0";

        public const string Name = "UniversalLayoutMath";

        public static string GetVersionInfo()
            => $"{Name} v{Version} - 纯数学逻辑类库，零依赖，跨领域通用";


        public static int RatioToPixel(int parentSize, double ratio)
            => ProportionalMath.RatioToPixel(parentSize, ratio);

        public static double PixelToRatio(int parentSize, int pixel)
            => ProportionalMath.PixelToRatio(parentSize, pixel);

        public static (int X, int Y, int Width, int Height) CalculateProportionalBounds(
            int parentWidth, int parentHeight,
            double left, double top,
            double? widthRatio, double? heightRatio,
            int defaultX, int defaultY, int defaultWidth, int defaultHeight)
            => ProportionalMath.CalculateProportionalBounds(
                parentWidth, parentHeight, left, top,
                widthRatio, heightRatio,
                defaultX, defaultY, defaultWidth, defaultHeight);

        public static bool IsValidProportionalPosition(double left, double top, double width, double height)
            => ProportionalMath.IsValidProportionalPosition(left, top, width, height);

        public static (double Left, double Top, double Width, double Height) ClampProportionalPosition(
            double left, double top, double width, double height)
            => ProportionalMath.ClampProportionalPosition(left, top, width, height);


        public static bool WouldOverlap(Rect a, Rect b)
            => OverlapMath.WouldOverlap(a, b);

        public static Rect CalculateOverlapRect(Rect a, Rect b)
            => OverlapMath.CalculateOverlapRect(a, b);

        public static double CalculateOverlapRatio(Rect a, Rect b, Rect overlap)
            => OverlapMath.CalculateOverlapRatio(a, b, overlap);

        public static OverlapLevel ClassifyOverlap(double ratio)
            => OverlapMath.ClassifyOverlap(ratio);

        public static OverlapPair? DetectOverlap(string nameA, Rect rectA, string nameB, Rect rectB)
            => OverlapMath.DetectOverlap(nameA, rectA, nameB, rectB);

        public static OverlapReport DetectOverlaps(List<OverlapMath.ElementDesc> elements)
            => OverlapMath.DetectOverlapsInElements(elements);

        public static OverlapReport DetectOverlapsProportional(List<OverlapMath.ProportionalElementDesc> elements)
            => OverlapMath.DetectOverlapsInProportionalElements(elements);


        public static int Clamp(int value, int min, int max)
            => ClampMath.Clamp(value, min, max);

        public static double Clamp(double value, double min, double max)
            => ClampMath.Clamp(value, min, max);

        public static int ClampSize(int size, int minSize = 1)
            => ClampMath.ClampSize(size, minSize);

        public static int ClampPosition(int position, int containerSize, int elementSize)
            => ClampMath.ClampPosition(position, containerSize, elementSize);

        public static (int X, int Y, int Width, int Height) ClampRect(
            int x, int y, int width, int height,
            int containerWidth, int containerHeight, int minSize = 1)
            => ClampMath.ClampRect(x, y, width, height, containerWidth, containerHeight, minSize);

        public static (int Width, int Height) ClampAspectRatio(
            int width, int height, double minAspectRatio, double maxAspectRatio)
            => ClampMath.ClampAspectRatio(width, height, minAspectRatio, maxAspectRatio);

        public static double Normalize(double value, double min, double max)
            => ClampMath.Normalize(value, min, max);

        public static double MapRange(double value, double fromMin, double fromMax, double toMin, double toMax)
            => ClampMath.MapRange(value, fromMin, fromMax, toMin, toMax);


        public static List<StackLayoutItem> CalculateStackLayout(
            int containerWidth, int containerHeight,
            int itemCount, StackLayoutConfig config)
            => StackLayoutMath.Calculate(containerWidth, containerHeight, itemCount, config);

        public static (int TotalWidth, int TotalHeight) CalculateStackTotalSize(
            int itemCount, StackLayoutConfig config, int defaultItemSize = 100)
            => StackLayoutMath.CalculateTotalSize(itemCount, config, defaultItemSize);


        public static ConflictGraph BuildConflictGraph(OverlapReport report, List<OverlapMath.ElementDesc> elements)
            => ConflictGraph.BuildFromReport(report, elements);

        public static List<string> GetConflictPriorityOrder(ConflictGraph graph)
            => graph.GetPriorityOrder();

        public static List<List<string>> GetConnectedComponents(ConflictGraph graph)
            => graph.GetConnectedComponents();


        public static MultiObjectiveOptimizer CreateOptimizer(int parentWidth, int parentHeight, int margin = 5)
            => new MultiObjectiveOptimizer(parentWidth, parentHeight, margin);

        public static FixScore? SelectBestFix(
            OverlapPair overlap,
            List<OverlapMath.ElementDesc> allElements,
            int parentWidth, int parentHeight, int margin = 5)
        {
            var optimizer = new MultiObjectiveOptimizer(parentWidth, parentHeight, margin);
            return optimizer.SelectBestStrategy(overlap, allElements);
        }


        public static GapPair? DetectGap(string nameA, Rect rectA, string nameB, Rect rectB)
            => OverlapMath.DetectGap(nameA, rectA, nameB, rectB);

        public static GapReport DetectGaps(List<OverlapMath.ElementDesc> elements, int minGap = 5)
            => OverlapMath.DetectGapsInElements(elements, minGap);

        public static GapReport DetectGapsProportional(
            List<OverlapMath.ProportionalElementDesc> elements,
            int parentWidth, int parentHeight, int minGap = 5)
            => OverlapMath.DetectGapsInProportionalElements(elements, parentWidth, parentHeight, minGap);


        public static List<BoundsViolation> DetectBoundsViolations(List<OverlapMath.ProportionalElementDesc> elements)
            => OverlapMath.DetectBoundsViolations(elements);

        public static List<BoundsViolation> DetectBoundsViolations(
            List<OverlapMath.ElementDesc> elements, int parentWidth, int parentHeight)
            => OverlapMath.DetectBoundsViolations(elements, parentWidth, parentHeight);


        public static OverlapReport DetectHeaderZoneOverlaps(List<HeaderZoneConfig> containers)
            => OverlapMath.DetectHeaderZoneOverlaps(containers);

        public static OverlapReport DetectHeaderZoneOverlapsProportional(
            List<ProportionalHeaderZoneConfig> containers)
            => OverlapMath.DetectHeaderZoneOverlapsProportional(containers);


        public static OverlapMath.ComprehensiveReport ComprehensiveDetect(
            List<OverlapMath.ProportionalElementDesc> elements,
            List<ProportionalHeaderZoneConfig>? headerZones = null,
            int parentWidth = 1920, int parentHeight = 1080,
            int minGap = 5)
            => OverlapMath.ComprehensiveDetect(elements, headerZones, parentWidth, parentHeight, minGap);

        public static OverlapMath.ComprehensiveReport ComprehensiveDetect(
            List<OverlapMath.ElementDesc> elements,
            List<HeaderZoneConfig>? headerZones = null,
            int parentWidth = 1920, int parentHeight = 1080,
            int minGap = 5)
            => OverlapMath.ComprehensiveDetect(elements, headerZones, parentWidth, parentHeight, minGap);


        public static OverlapMath.ContainerAwareOverlapReport DetectContainerAwareOverlaps(
            List<OverlapMath.ContainerAwareElementDesc> elements,
            Dictionary<string, OverlapMath.ContainerBoundsInfo>? containerBounds = null,
            int defaultHeaderHeight = 25)
            => OverlapMath.DetectContainerAwareOverlaps(elements, containerBounds, defaultHeaderHeight);

    }
}
