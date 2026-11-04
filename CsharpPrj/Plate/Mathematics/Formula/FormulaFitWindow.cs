namespace MathematicalAlgorithms.Formula;

/// <summary>拟合窗口：时间范围 + 条件快照。</summary>
public sealed record FormulaFitWindow(
    string From,
    string To,
    IReadOnlyDictionary<string, string> Conditions)
{
    public FormulaFitWindow Normalize()
    {
        var sorted = new SortedDictionary<string, string>(StringComparer.Ordinal);
        if (Conditions is not null)
        {
            foreach (KeyValuePair<string, string> pair in Conditions)
                sorted[pair.Key] = pair.Value;
        }

        return this with { Conditions = sorted };
    }
}
