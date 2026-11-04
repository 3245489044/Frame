namespace MathematicalAlgorithms.Fitting;

/// <summary>
/// 规范化样本集：拒绝非有限值，并按 X、再按 Y 排序，使拟合结果与输入顺序无关。
/// <para>重复 X 保留（不静默去重），由拟合器按样本等权处理。</para>
/// </summary>
public sealed class FitSampleSet
{
    private readonly FitSample[] _samples;

    private FitSampleSet(FitSample[] samples)
    {
        _samples = samples;
        MinX = samples[0].X;
        MaxX = samples[^1].X;

        double minY = double.PositiveInfinity;
        double maxY = double.NegativeInfinity;
        foreach (FitSample sample in samples)
        {
            if (sample.Y < minY) minY = sample.Y;
            if (sample.Y > maxY) maxY = sample.Y;
        }

        MinY = minY;
        MaxY = maxY;
    }

    public IReadOnlyList<FitSample> Samples => _samples;

    public int Count => _samples.Length;

    public double MinX { get; }

    public double MaxX { get; }

    public double MinY { get; }

    public double MaxY { get; }

    /// <summary>至少两个不同的 X，否则斜率/分母类模型不可辨识。</summary>
    public bool HasDistinctX => Count > 1 && MaxX > MinX;

    public static bool TryCreate(IEnumerable<FitSample>? source, out FitSampleSet? set, out string? error)
    {
        set = null;
        error = null;

        if (source is null)
        {
            error = "样本集合为空。";
            return false;
        }

        var list = new List<FitSample>();
        foreach (FitSample sample in source)
        {
            if (!sample.IsFinite)
            {
                error = $"样本含非有限值：X={sample.X}, Y={sample.Y}。";
                return false;
            }

            list.Add(sample);
        }

        if (list.Count == 0)
        {
            error = "样本集合为空。";
            return false;
        }

        list.Sort(static (a, b) =>
        {
            int byX = a.X.CompareTo(b.X);
            return byX != 0 ? byX : a.Y.CompareTo(b.Y);
        });

        set = new FitSampleSet(list.ToArray());
        return true;
    }
}
