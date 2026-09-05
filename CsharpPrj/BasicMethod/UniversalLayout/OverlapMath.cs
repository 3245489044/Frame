#nullable enable
using System;
using System.Collections.Generic;

namespace UniversalLayoutMath
{

    public enum OverlapLevel
    {
        None = 0,
        Slight = 1,
        Moderate = 2,
        Severe = 3,
        Complete = 4
    }


    public readonly struct Rect
    {
        public int X { get; }
        public int Y { get; }
        public int Width { get; }
        public int Height { get; }
        public int Left => X;
        public int Top => Y;
        public int Right => X + Width;
        public int Bottom => Y + Height;
        public int Area => Width * Height;
        public bool IsEmpty => Width <= 0 || Height <= 0;

        public Rect(int x, int y, int width, int height)
        {
            X = x;
            Y = y;
            Width = width;
            Height = height;
        }

        public override string ToString()
            => $"({X},{Y},{Width}x{Height})";
    }

    public readonly struct ProportionalRect
    {
        public double Left { get; }
        public double Top { get; }
        public double Width { get; }
        public double Height { get; }
        public double Right => Left + Width;
        public double Bottom => Top + Height;
        public double Area => Width * Height;
        public bool IsValid => Left >= 0 && Top >= 0 && Width > 0 && Height > 0 &&
                               Left + Width <= 1 && Top + Height <= 1;

        public ProportionalRect(double left, double top, double width, double height)
        {
            Left = left;
            Top = top;
            Width = width;
            Height = height;
        }

        public override string ToString()
            => $"({Left:F3},{Top:F3},{Width:F3}x{Height:F3})";
    }


    public class OverlapPair
    {
        public string ElementA { get; set; } = string.Empty;
        public string ElementB { get; set; } = string.Empty;
        public Rect RectA { get; set; }
        public Rect RectB { get; set; }
        public Rect OverlapRect { get; set; }
        public int OverlapArea { get; set; }
        public double OverlapRatio { get; set; }
        public OverlapLevel Level { get; set; }
        public bool IsProportionalSpace { get; set; }

        public override string ToString()
            => $"'{ElementA}' & '{ElementB}': 重叠区域={OverlapRect.Width}x{OverlapRect.Height}, " +
               $"重叠率={OverlapRatio:P1}, 级别={Level}";
    }

    public class OverlapReport
    {
        public bool HasOverlap { get; set; }
        public List<OverlapPair> Overlaps { get; set; } = new List<OverlapPair>();
        public double MaxOverlapRatio { get; set; }
        public OverlapLevel WorstLevel { get; set; } = OverlapLevel.None;
        public int OverlappingElementCount { get; set; }
        public DateTime DetectedAt { get; set; } = DateTime.Now;

        public string GetSummary()
        {
            if (!HasOverlap) return "无重叠";
            return $"检测到 {Overlaps.Count} 处重叠，涉及 {OverlappingElementCount} 个元素，" +
                   $"最严重级别: {WorstLevel}，最大重叠率: {MaxOverlapRatio:P1}";
        }
    }


    public class GapPair
    {
        public string ElementA { get; set; } = string.Empty;
        public string ElementB { get; set; } = string.Empty;
        public int GapX { get; set; }
        public int GapY { get; set; }
        public int MinGap => Math.Min(Math.Abs(GapX), Math.Abs(GapY));
        public bool IsOverlapping => GapX < 0 || GapY < 0;
        public bool IsTooClose(int minGap = 5) => (GapX >= 0 && GapY >= 0) && MinGap < minGap;

        public override string ToString()
            => $"'{ElementA}' & '{ElementB}': 水平间距={GapX}, 垂直间距={GapY}";
    }

    public class GapReport
    {
        public bool HasInsufficientGap { get; set; }
        public List<GapPair> Gaps { get; set; } = new List<GapPair>();
        public int MinGapThreshold { get; set; } = 5;
        public DateTime DetectedAt { get; set; } = DateTime.Now;

        public string GetSummary()
        {
            if (!HasInsufficientGap) return "间距充足";
            return $"检测到 {Gaps.Count} 处间距不足（阈值={MinGapThreshold}px）";
        }
    }


    public class BoundsViolation
    {
        public string ElementName { get; set; } = string.Empty;
        public string ViolationType { get; set; } = string.Empty;
        public double Offset { get; set; }
        public bool IsProportionalSpace { get; set; }

        public string Description => $"'{ElementName}' {ViolationType} 超出边界 {Offset:F3}";

        public override string ToString() => Description;
    }

    public class BoundsReport
    {
        public bool HasViolations { get; set; }
        public List<BoundsViolation> Violations { get; set; } = new List<BoundsViolation>();
        public DateTime DetectedAt { get; set; } = DateTime.Now;

        public string GetSummary()
        {
            if (!HasViolations) return "无越界";
            return $"检测到 {Violations.Count} 处越界";
        }
    }


    public class HeaderZoneConfig
    {
        public string ContainerName { get; set; } = string.Empty;
        public Rect HeaderRect { get; set; }
        public List<Rect> ChildRects { get; set; } = new List<Rect>();
        public List<string> ChildNames { get; set; } = new List<string>();
    }

    public class ProportionalHeaderZoneConfig
    {
        public string ContainerName { get; set; } = string.Empty;
        public ProportionalRect HeaderRect { get; set; }
        public List<ProportionalRect> ChildRects { get; set; } = new List<ProportionalRect>();
        public List<string> ChildNames { get; set; } = new List<string>();
    }


    public static class OverlapMath
    {

        public static bool WouldOverlap(Rect a, Rect b)
        {
            return !(a.Right <= b.Left || b.Right <= a.Left ||
                     a.Bottom <= b.Top || b.Bottom <= a.Top);
        }

        public static bool WouldOverlap(int ax, int ay, int aw, int ah,
                                        int bx, int by, int bw, int bh)
        {
            return WouldOverlap(new Rect(ax, ay, aw, ah), new Rect(bx, by, bw, bh));
        }

        public static Rect CalculateOverlapRect(Rect a, Rect b)
        {
            int x = Math.Max(a.Left, b.Left);
            int y = Math.Max(a.Top, b.Top);
            int right = Math.Min(a.Right, b.Right);
            int bottom = Math.Min(a.Bottom, b.Bottom);

            if (right <= x || bottom <= y)
                return new Rect(0, 0, 0, 0);

            return new Rect(x, y, right - x, bottom - y);
        }

        public static double CalculateOverlapRatio(Rect a, Rect b, Rect overlap)
        {
            if (overlap.IsEmpty) return 0;

            int overlapArea = overlap.Area;
            int areaA = a.Area;
            int areaB = b.Area;
            int smallerArea = Math.Min(areaA, areaB);

            if (smallerArea <= 0) return 0;
            return (double)overlapArea / smallerArea;
        }

        public static OverlapLevel ClassifyOverlap(double ratio)
        {
            if (ratio <= 0) return OverlapLevel.None;
            if (ratio <= 0.10) return OverlapLevel.Slight;
            if (ratio <= 0.30) return OverlapLevel.Moderate;
            if (ratio <= 0.60) return OverlapLevel.Severe;
            return OverlapLevel.Complete;
        }

        public static OverlapPair? DetectOverlap(
            string nameA, Rect rectA,
            string nameB, Rect rectB)
        {
            if (!WouldOverlap(rectA, rectB)) return null;

            var overlapRect = CalculateOverlapRect(rectA, rectB);
            var ratio = CalculateOverlapRatio(rectA, rectB, overlapRect);
            var level = ClassifyOverlap(ratio);

            return new OverlapPair
            {
                ElementA = nameA,
                ElementB = nameB,
                RectA = rectA,
                RectB = rectB,
                OverlapRect = overlapRect,
                OverlapArea = overlapRect.Area,
                OverlapRatio = ratio,
                Level = level,
                IsProportionalSpace = false
            };
        }


        public static bool WouldOverlapProportional(
            double aL, double aT, double aW, double aH,
            double bL, double bT, double bW, double bH)
        {
            return !(aL + aW <= bL || bL + bW <= aL ||
                     aT + aH <= bT || bT + bH <= aT);
        }

        public static bool WouldOverlapProportional(ProportionalRect a, ProportionalRect b)
        {
            return WouldOverlapProportional(
                a.Left, a.Top, a.Width, a.Height,
                b.Left, b.Top, b.Width, b.Height);
        }

        public static OverlapPair? DetectOverlapProportional(
            string nameA, ProportionalRect rectA,
            string nameB, ProportionalRect rectB)
        {
            if (!WouldOverlapProportional(rectA, rectB)) return null;

            double overlapLeft = Math.Max(rectA.Left, rectB.Left);
            double overlapTop = Math.Max(rectA.Top, rectB.Top);
            double overlapRight = Math.Min(rectA.Right, rectB.Right);
            double overlapBottom = Math.Min(rectA.Bottom, rectB.Bottom);
            double overlapW = Math.Max(0, overlapRight - overlapLeft);
            double overlapH = Math.Max(0, overlapBottom - overlapTop);

            double area1 = rectA.Area;
            double area2 = rectB.Area;
            double overlapArea = overlapW * overlapH;
            double smallerArea = Math.Min(area1, area2);
            double ratio = smallerArea > 0 ? overlapArea / smallerArea : 0;
            var level = ClassifyOverlap(ratio);

            return new OverlapPair
            {
                ElementA = nameA,
                ElementB = nameB,
                OverlapArea = 0,
                OverlapRatio = ratio,
                Level = level,
                IsProportionalSpace = true
            };
        }


        public class ElementDesc
        {
            public string Name { get; set; } = string.Empty;
            public Rect Rect { get; set; }
        }

        public static OverlapReport DetectOverlapsInElements(List<ElementDesc> elements)
        {
            var report = new OverlapReport();

            if (elements.Count < 2) return report;

            var overlappingElements = new HashSet<string>();

            for (int i = 0; i < elements.Count; i++)
            {
                for (int j = i + 1; j < elements.Count; j++)
                {
                    var pair = DetectOverlap(
                        elements[i].Name, elements[i].Rect,
                        elements[j].Name, elements[j].Rect);

                    if (pair == null) continue;

                    report.Overlaps.Add(pair);
                    overlappingElements.Add(elements[i].Name);
                    overlappingElements.Add(elements[j].Name);

                    if (pair.OverlapRatio > report.MaxOverlapRatio)
                        report.MaxOverlapRatio = pair.OverlapRatio;
                    if (pair.Level > report.WorstLevel)
                        report.WorstLevel = pair.Level;
                }
            }

            report.HasOverlap = report.Overlaps.Count > 0;
            report.OverlappingElementCount = overlappingElements.Count;
            return report;
        }

        public class ProportionalElementDesc
        {
            public string Name { get; set; } = string.Empty;
            public ProportionalRect Rect { get; set; }
        }

        public static OverlapReport DetectOverlapsInProportionalElements(
            List<ProportionalElementDesc> elements)
        {
            var report = new OverlapReport();
            if (elements.Count < 2) return report;

            var overlappingElements = new HashSet<string>();

            for (int i = 0; i < elements.Count; i++)
            {
                for (int j = i + 1; j < elements.Count; j++)
                {
                    var pair = DetectOverlapProportional(
                        elements[i].Name, elements[i].Rect,
                        elements[j].Name, elements[j].Rect);

                    if (pair == null) continue;

                    report.Overlaps.Add(pair);
                    overlappingElements.Add(elements[i].Name);
                    overlappingElements.Add(elements[j].Name);

                    if (pair.OverlapRatio > report.MaxOverlapRatio)
                        report.MaxOverlapRatio = pair.OverlapRatio;
                    if (pair.Level > report.WorstLevel)
                        report.WorstLevel = pair.Level;
                }
            }

            report.HasOverlap = report.Overlaps.Count > 0;
            report.OverlappingElementCount = overlappingElements.Count;
            return report;
        }


        public static GapPair? DetectGap(
            string nameA, Rect rectA,
            string nameB, Rect rectB)
        {
            int gapX = Math.Max(rectA.Left - rectB.Right, rectB.Left - rectA.Right);
            int gapY = Math.Max(rectA.Top - rectB.Bottom, rectB.Top - rectA.Bottom);

            return new GapPair
            {
                ElementA = nameA,
                ElementB = nameB,
                GapX = gapX,
                GapY = gapY
            };
        }

        public static GapReport DetectGapsInElements(
            List<ElementDesc> elements, int minGap = 5)
        {
            var report = new GapReport
            {
                MinGapThreshold = minGap
            };

            if (elements.Count < 2) return report;

            for (int i = 0; i < elements.Count; i++)
            {
                for (int j = i + 1; j < elements.Count; j++)
                {
                    var gap = DetectGap(
                        elements[i].Name, elements[i].Rect,
                        elements[j].Name, elements[j].Rect);

                    if (gap != null && gap.IsTooClose(minGap))
                    {
                        report.Gaps.Add(gap);
                    }
                }
            }

            report.HasInsufficientGap = report.Gaps.Count > 0;
            return report;
        }


        public static GapPair? DetectGapProportional(
            string nameA, ProportionalRect rectA,
            string nameB, ProportionalRect rectB,
            int parentWidth, int parentHeight)
        {
            int ax = ProportionalMath.RatioToPixel(parentWidth, rectA.Left);
            int ay = ProportionalMath.RatioToPixel(parentHeight, rectA.Top);
            int aw = ProportionalMath.RatioToPixel(parentWidth, rectA.Width);
            int ah = ProportionalMath.RatioToPixel(parentHeight, rectA.Height);

            int bx = ProportionalMath.RatioToPixel(parentWidth, rectB.Left);
            int by = ProportionalMath.RatioToPixel(parentHeight, rectB.Top);
            int bw = ProportionalMath.RatioToPixel(parentWidth, rectB.Width);
            int bh = ProportionalMath.RatioToPixel(parentHeight, rectB.Height);

            var rectApx = new Rect(ax, ay, aw, ah);
            var rectBpx = new Rect(bx, by, bw, bh);

            return DetectGap(nameA, rectApx, nameB, rectBpx);
        }

        public static GapReport DetectGapsInProportionalElements(
            List<ProportionalElementDesc> elements,
            int parentWidth, int parentHeight, int minGap = 5)
        {
            var report = new GapReport
            {
                MinGapThreshold = minGap
            };

            if (elements.Count < 2) return report;

            for (int i = 0; i < elements.Count; i++)
            {
                for (int j = i + 1; j < elements.Count; j++)
                {
                    var gap = DetectGapProportional(
                        elements[i].Name, elements[i].Rect,
                        elements[j].Name, elements[j].Rect,
                        parentWidth, parentHeight);

                    if (gap != null && gap.IsTooClose(minGap))
                    {
                        report.Gaps.Add(gap);
                    }
                }
            }

            report.HasInsufficientGap = report.Gaps.Count > 0;
            return report;
        }


        public static List<BoundsViolation> DetectBoundsViolations(
            List<ProportionalElementDesc> elements)
        {
            var violations = new List<BoundsViolation>();

            foreach (var e in elements)
            {
                if (e.Rect.Left < 0)
                    violations.Add(new BoundsViolation
                    {
                        ElementName = e.Name,
                        ViolationType = "Left",
                        Offset = -e.Rect.Left,
                        IsProportionalSpace = true
                    });
                if (e.Rect.Top < 0)
                    violations.Add(new BoundsViolation
                    {
                        ElementName = e.Name,
                        ViolationType = "Top",
                        Offset = -e.Rect.Top,
                        IsProportionalSpace = true
                    });
                if (e.Rect.Right > 1)
                    violations.Add(new BoundsViolation
                    {
                        ElementName = e.Name,
                        ViolationType = "Right",
                        Offset = e.Rect.Right - 1,
                        IsProportionalSpace = true
                    });
                if (e.Rect.Bottom > 1)
                    violations.Add(new BoundsViolation
                    {
                        ElementName = e.Name,
                        ViolationType = "Bottom",
                        Offset = e.Rect.Bottom - 1,
                        IsProportionalSpace = true
                    });
            }

            return violations;
        }

        public static List<BoundsViolation> DetectBoundsViolations(
            List<ElementDesc> elements, int parentWidth, int parentHeight)
        {
            var violations = new List<BoundsViolation>();

            foreach (var e in elements)
            {
                if (e.Rect.Left < 0)
                    violations.Add(new BoundsViolation
                    {
                        ElementName = e.Name,
                        ViolationType = "Left",
                        Offset = -e.Rect.Left,
                        IsProportionalSpace = false
                    });
                if (e.Rect.Top < 0)
                    violations.Add(new BoundsViolation
                    {
                        ElementName = e.Name,
                        ViolationType = "Top",
                        Offset = -e.Rect.Top,
                        IsProportionalSpace = false
                    });
                if (e.Rect.Right > parentWidth)
                    violations.Add(new BoundsViolation
                    {
                        ElementName = e.Name,
                        ViolationType = "Right",
                        Offset = e.Rect.Right - parentWidth,
                        IsProportionalSpace = false
                    });
                if (e.Rect.Bottom > parentHeight)
                    violations.Add(new BoundsViolation
                    {
                        ElementName = e.Name,
                        ViolationType = "Bottom",
                        Offset = e.Rect.Bottom - parentHeight,
                        IsProportionalSpace = false
                    });
            }

            return violations;
        }


        public static OverlapReport DetectHeaderZoneOverlaps(
            List<HeaderZoneConfig> containers)
        {
            var report = new OverlapReport();
            var overlappingElements = new HashSet<string>();

            foreach (var container in containers)
            {
                for (int i = 0; i < container.ChildRects.Count; i++)
                {
                    var childRect = container.ChildRects[i];
                    var childName = i < container.ChildNames.Count
                        ? container.ChildNames[i]
                        : $"child_{i}";

                    var pair = DetectOverlap(
                        $"{container.ContainerName}.Header", container.HeaderRect,
                        $"{container.ContainerName}.{childName}", childRect);

                    if (pair != null)
                    {
                        report.Overlaps.Add(pair);
                        overlappingElements.Add($"{container.ContainerName}.Header");
                        overlappingElements.Add($"{container.ContainerName}.{childName}");

                        if (pair.OverlapRatio > report.MaxOverlapRatio)
                            report.MaxOverlapRatio = pair.OverlapRatio;
                        if (pair.Level > report.WorstLevel)
                            report.WorstLevel = pair.Level;
                    }
                }
            }

            report.HasOverlap = report.Overlaps.Count > 0;
            report.OverlappingElementCount = overlappingElements.Count;
            return report;
        }

        public static OverlapReport DetectHeaderZoneOverlapsProportional(
            List<ProportionalHeaderZoneConfig> containers)
        {
            var report = new OverlapReport();
            var overlappingElements = new HashSet<string>();

            foreach (var container in containers)
            {
                for (int i = 0; i < container.ChildRects.Count; i++)
                {
                    var childRect = container.ChildRects[i];
                    var childName = i < container.ChildNames.Count
                        ? container.ChildNames[i]
                        : $"child_{i}";

                    var pair = DetectOverlapProportional(
                        $"{container.ContainerName}.Header", container.HeaderRect,
                        $"{container.ContainerName}.{childName}", childRect);

                    if (pair != null)
                    {
                        report.Overlaps.Add(pair);
                        overlappingElements.Add($"{container.ContainerName}.Header");
                        overlappingElements.Add($"{container.ContainerName}.{childName}");

                        if (pair.OverlapRatio > report.MaxOverlapRatio)
                            report.MaxOverlapRatio = pair.OverlapRatio;
                        if (pair.Level > report.WorstLevel)
                            report.WorstLevel = pair.Level;
                    }
                }
            }

            report.HasOverlap = report.Overlaps.Count > 0;
            report.OverlappingElementCount = overlappingElements.Count;
            return report;
        }


        public class ComprehensiveReport
        {
            public OverlapReport OverlapReport { get; set; } = new OverlapReport();
            public GapReport GapReport { get; set; } = new GapReport();
            public BoundsReport BoundsReport { get; set; } = new BoundsReport();
            public OverlapReport HeaderZoneReport { get; set; } = new OverlapReport();
            public DateTime DetectedAt { get; set; } = DateTime.Now;

            public bool HasAnyIssue =>
                OverlapReport.HasOverlap ||
                GapReport.HasInsufficientGap ||
                BoundsReport.HasViolations ||
                HeaderZoneReport.HasOverlap;

            public string GetSummary()
            {
                var parts = new List<string>();
                if (OverlapReport.HasOverlap)
                    parts.Add($"重叠: {OverlapReport.Overlaps.Count}处");
                if (GapReport.HasInsufficientGap)
                    parts.Add($"间距不足: {GapReport.Gaps.Count}处");
                if (BoundsReport.HasViolations)
                    parts.Add($"越界: {BoundsReport.Violations.Count}处");
                if (HeaderZoneReport.HasOverlap)
                    parts.Add($"标题区重叠: {HeaderZoneReport.Overlaps.Count}处");

                return parts.Count > 0
                    ? $"检测到问题: {string.Join(", ", parts)}"
                    : "✅ 无任何问题";
            }
        }

        public static ComprehensiveReport ComprehensiveDetect(
            List<ProportionalElementDesc> elements,
            List<ProportionalHeaderZoneConfig>? headerZones = null,
            int parentWidth = 1920, int parentHeight = 1080,
            int minGap = 5)
        {
            var result = new ComprehensiveReport();

            result.OverlapReport = DetectOverlapsInProportionalElements(elements);

            result.GapReport = DetectGapsInProportionalElements(
                elements, parentWidth, parentHeight, minGap);

            var boundsViolations = DetectBoundsViolations(elements);
            result.BoundsReport.HasViolations = boundsViolations.Count > 0;
            result.BoundsReport.Violations = boundsViolations;

            if (headerZones != null && headerZones.Count > 0)
            {
                result.HeaderZoneReport = DetectHeaderZoneOverlapsProportional(headerZones);
            }

            return result;
        }

        public static ComprehensiveReport ComprehensiveDetect(
            List<ElementDesc> elements,
            List<HeaderZoneConfig>? headerZones = null,
            int parentWidth = 1920, int parentHeight = 1080,
            int minGap = 5)
        {
            var result = new ComprehensiveReport();

            result.OverlapReport = DetectOverlapsInElements(elements);

            result.GapReport = DetectGapsInElements(elements, minGap);

            var boundsViolations = DetectBoundsViolations(elements, parentWidth, parentHeight);
            result.BoundsReport.HasViolations = boundsViolations.Count > 0;
            result.BoundsReport.Violations = boundsViolations;

            if (headerZones != null && headerZones.Count > 0)
            {
                result.HeaderZoneReport = DetectHeaderZoneOverlaps(headerZones);
            }

            return result;
        }


        public class ContainerAwareElementDesc
        {
            public string Name { get; set; } = string.Empty;
            public string ContainerName { get; set; } = string.Empty;
            public Rect Rect { get; set; }
        }

        public class ContainerAwareOverlapReport
        {
            public OverlapReport SameContainerOverlaps { get; set; } = new();
            public OverlapReport CrossContainerOverlaps { get; set; } = new();
            public OverlapReport HeaderZoneOverlaps { get; set; } = new();
            public bool HasAnyOverlap =>
                SameContainerOverlaps.HasOverlap ||
                CrossContainerOverlaps.HasOverlap ||
                HeaderZoneOverlaps.HasOverlap;
            public string GetSummary()
            {
                return $"同容器重叠: {SameContainerOverlaps.Overlaps.Count}处, " +
                       $"跨容器重叠: {CrossContainerOverlaps.Overlaps.Count}处, " +
                       $"标题区重叠: {HeaderZoneOverlaps.Overlaps.Count}处";
            }
        }

        public class ContainerBoundsInfo
        {
            public string ContainerName { get; set; } = string.Empty;
            public Rect Bounds { get; set; }
            public int HeaderHeight { get; set; } = 25;
        }

        public static ContainerAwareOverlapReport DetectContainerAwareOverlaps(
            List<ContainerAwareElementDesc> elements,
            Dictionary<string, ContainerBoundsInfo>? containerBounds = null,
            int defaultHeaderHeight = 25)
        {
            var report = new ContainerAwareOverlapReport();

            if (elements.Count < 2)
                return report;

            var overlappingSameContainer = new HashSet<string>();
            var overlappingCrossContainer = new HashSet<string>();
            var overlappingHeaderZone = new HashSet<string>();

            for (int i = 0; i < elements.Count; i++)
            {
                var e1 = elements[i];
                for (int j = i + 1; j < elements.Count; j++)
                {
                    var e2 = elements[j];

                    if (!WouldOverlap(e1.Rect, e2.Rect))
                        continue;

                    var overlapRect = CalculateOverlapRect(e1.Rect, e2.Rect);
                    double ratio = CalculateOverlapRatio(e1.Rect, e2.Rect, overlapRect);
                    var level = ClassifyOverlap(ratio);

                    var pair = new OverlapPair
                    {
                        ElementA = e1.Name,
                        ElementB = e2.Name,
                        RectA = e1.Rect,
                        RectB = e2.Rect,
                        OverlapRect = overlapRect,
                        OverlapArea = overlapRect.Area,
                        OverlapRatio = ratio,
                        Level = level
                    };

                    bool sameContainer = !string.IsNullOrEmpty(e1.ContainerName) &&
                                         !string.IsNullOrEmpty(e2.ContainerName) &&
                                         e1.ContainerName == e2.ContainerName;

                    if (sameContainer)
                    {
                        report.SameContainerOverlaps.Overlaps.Add(pair);
                        overlappingSameContainer.Add(e1.Name);
                        overlappingSameContainer.Add(e2.Name);
                        if (ratio > report.SameContainerOverlaps.MaxOverlapRatio)
                            report.SameContainerOverlaps.MaxOverlapRatio = ratio;
                        if (level > report.SameContainerOverlaps.WorstLevel)
                            report.SameContainerOverlaps.WorstLevel = level;
                    }
                    else
                    {
                        report.CrossContainerOverlaps.Overlaps.Add(pair);
                        overlappingCrossContainer.Add(e1.Name);
                        overlappingCrossContainer.Add(e2.Name);
                        if (ratio > report.CrossContainerOverlaps.MaxOverlapRatio)
                            report.CrossContainerOverlaps.MaxOverlapRatio = ratio;
                        if (level > report.CrossContainerOverlaps.WorstLevel)
                            report.CrossContainerOverlaps.WorstLevel = level;
                    }
                }

                if (containerBounds != null && !string.IsNullOrEmpty(e1.ContainerName))
                {
                    if (containerBounds.TryGetValue(e1.ContainerName, out var containerInfo))
                    {
                        var headerRect = new Rect(
                            containerInfo.Bounds.X,
                            containerInfo.Bounds.Y,
                            containerInfo.Bounds.Width,
                            Math.Min(containerInfo.HeaderHeight, containerInfo.Bounds.Height));

                        if (WouldOverlap(e1.Rect, headerRect))
                        {
                            var headerOverlap = CalculateOverlapRect(e1.Rect, headerRect);
                            double headerRatio = CalculateOverlapRatio(e1.Rect, headerRect, headerOverlap);
                            var headerLevel = ClassifyOverlap(headerRatio);

                            var headerPair = new OverlapPair
                            {
                                ElementA = e1.Name,
                                ElementB = $"{e1.ContainerName}.Header",
                                RectA = e1.Rect,
                                RectB = headerRect,
                                OverlapRect = headerOverlap,
                                OverlapArea = headerOverlap.Area,
                                OverlapRatio = headerRatio,
                                Level = headerLevel
                            };

                            report.HeaderZoneOverlaps.Overlaps.Add(headerPair);
                            overlappingHeaderZone.Add(e1.Name);
                            if (headerRatio > report.HeaderZoneOverlaps.MaxOverlapRatio)
                                report.HeaderZoneOverlaps.MaxOverlapRatio = headerRatio;
                            if (headerLevel > report.HeaderZoneOverlaps.WorstLevel)
                                report.HeaderZoneOverlaps.WorstLevel = headerLevel;
                        }
                    }
                }
            }

            report.SameContainerOverlaps.HasOverlap = report.SameContainerOverlaps.Overlaps.Count > 0;
            report.SameContainerOverlaps.OverlappingElementCount = overlappingSameContainer.Count;
            report.CrossContainerOverlaps.HasOverlap = report.CrossContainerOverlaps.Overlaps.Count > 0;
            report.CrossContainerOverlaps.OverlappingElementCount = overlappingCrossContainer.Count;
            report.HeaderZoneOverlaps.HasOverlap = report.HeaderZoneOverlaps.Overlaps.Count > 0;
            report.HeaderZoneOverlaps.OverlappingElementCount = overlappingHeaderZone.Count;

            return report;
        }

    }
}


