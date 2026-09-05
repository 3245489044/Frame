namespace JsonUIBeautifier.Utils;

public static class LayoutMath
{
    public static double AlignToGrid(double value, double gridSize)
    {
        if (gridSize <= 0) return value;
        return Math.Round(value / gridSize) * gridSize;
    }

    public static (double Left, double Top, double Width, double Height) AlignLayout(
        double left, double top, double width, double height, double gridSize)
    {
        return (
            AlignToGrid(left, gridSize),
            AlignToGrid(top, gridSize),
            AlignToGrid(width, gridSize),
            AlignToGrid(height, gridSize)
        );
    }

    public static double CalculateSpacing(
        double left1, double top1, double width1, double height1,
        double left2, double top2, double width2, double height2)
    {
        var right1 = left1 + width1;
        var bottom1 = top1 + height1;
        var right2 = left2 + width2;
        var bottom2 = top2 + height2;

        var hSpacing = left2 > right1 ? left2 - right1 : left1 - right2;
        var vSpacing = top2 > bottom1 ? top2 - bottom1 : top1 - bottom2;

        if (hSpacing < 0 && vSpacing < 0)
        {
            return hSpacing * vSpacing;
        }

        var positiveSpacings = new[] { hSpacing, vSpacing }.Where(s => s > 0).ToArray();
        return positiveSpacings.Length > 0 ? positiveSpacings.Min() : 0;
    }


    public static double CalculateSpacingConsistency(List<(double L, double T, double W, double H)> layouts)
    {
        if (layouts.Count < 3) return 10.0;

        var spacings = new List<double>();
        for (int i = 0; i < layouts.Count - 1; i++)
        {
            for (int j = i + 1; j < layouts.Count; j++)
            {
                var s = CalculateSpacing(
                    layouts[i].L, layouts[i].T, layouts[i].W, layouts[i].H,
                    layouts[j].L, layouts[j].T, layouts[j].W, layouts[j].H);
                if (s > 0) spacings.Add(s);
            }
        }

        if (spacings.Count < 2) return 10.0;

        var avg = spacings.Average();
        var variance = spacings.Select(s => Math.Pow(s - avg, 2)).Average();
        var stdDev = Math.Sqrt(variance);

        var score = Math.Max(0, 10 - stdDev * 20);
        return Math.Round(Math.Min(10, score), 1);
    }

    public static List<(int Index1, int Index2, double OverlapArea)> DetectOverlaps(
        List<(double L, double T, double W, double H)> layouts)
    {
        var overlaps = new List<(int, int, double)>();
        for (int i = 0; i < layouts.Count; i++)
        {
            for (int j = i + 1; j < layouts.Count; j++)
            {
                var overlap = CalculateOverlap(layouts[i], layouts[j]);
                if (overlap > 0)
                    overlaps.Add((i, j, overlap));
            }
        }
        return overlaps;
    }

    public static double CalculateOverlap(

        (double L, double T, double W, double H) a,
        (double L, double T, double W, double H) b)
    {
        var xOverlap = Math.Max(0, Math.Min(a.L + a.W, b.L + b.W) - Math.Max(a.L, b.L));
        var yOverlap = Math.Max(0, Math.Min(a.T + a.H, b.T + b.H) - Math.Max(a.T, b.T));
        return xOverlap * yOverlap;
    }

    public static double InferVerticalSpacing(List<double> tops, List<double> heights)
    {
        if (tops.Count < 2) return 0.05;

        var spacings = new List<double>();
        for (int i = 0; i < tops.Count - 1; i++)
        {
            var spacing = tops[i + 1] - (tops[i] + heights[i]);
            if (spacing > 0) spacings.Add(spacing);
        }

        if (spacings.Count == 0) return 0.05;
        return spacings.GroupBy(s => Math.Round(s, 2))
            .OrderByDescending(g => g.Count())
            .First().Key;
    }

    public static double InferHorizontalSpacing(List<double> lefts, List<double> widths)
    {
        if (lefts.Count < 2) return 0.02;

        var spacings = new List<double>();
        for (int i = 0; i < lefts.Count - 1; i++)
        {
            var spacing = lefts[i + 1] - (lefts[i] + widths[i]);
            if (spacing > 0) spacings.Add(spacing);
        }

        if (spacings.Count == 0) return 0.02;
        return spacings.GroupBy(s => Math.Round(s, 2))
            .OrderByDescending(g => g.Count())
            .First().Key;
    }

    public static double CalculateLayoutQuality(List<(double L, double T, double W, double H)> layouts)
    {
        if (layouts.Count == 0) return 10.0;

        double score = 10.0;

        var overlaps = DetectOverlaps(layouts);
        if (overlaps.Count > 0)
            score -= overlaps.Count * 2.0;

        foreach (var (l, t, w, h) in layouts)
        {
            if (l < 0 || t < 0 || l + w > 1 || t + h > 1)
                score -= 1.0;
        }

        foreach (var (l, t, w, h) in layouts)
        {
            if (w <= 0 || h <= 0) score -= 2.0;
            if (w > 1 || h > 1) score -= 1.0;
        }

        return Math.Round(Math.Max(0, score), 1);
    }
}
