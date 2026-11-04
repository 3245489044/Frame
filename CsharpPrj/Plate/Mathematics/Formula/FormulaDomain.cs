namespace MathematicalAlgorithms.Formula;

/// <summary>
/// 公式适用域：输入名 → 区间/枚举。
/// <para>越域只返回原因，不做夹取、不外推；顺序按输入名 Ordinal 排序，保证诊断可复现。</para>
/// </summary>
public sealed record FormulaDomain(
    IReadOnlyDictionary<string, FormulaRange> Ranges,
    FormulaGuard Guard = FormulaGuard.Refuse)
{
    public static FormulaDomain Empty { get; } =
        new(new SortedDictionary<string, FormulaRange>(StringComparer.Ordinal));

    public FormulaRange? GetRange(string name)
        => Ranges is not null && Ranges.TryGetValue(name, out FormulaRange? range) ? range : null;

    public bool TryValidate(
        IReadOnlyDictionary<string, double> inputs,
        out string? violatedInput,
        out string? reason)
    {
        violatedInput = null;
        reason = null;

        if (Ranges is null)
            return true;

        foreach (string key in Ranges.Keys.OrderBy(static k => k, StringComparer.Ordinal))
        {
            if (!inputs.TryGetValue(key, out double value))
            {
                violatedInput = key;
                reason = $"缺少输入 {key}。";
                return false;
            }

            if (!double.IsFinite(value))
            {
                violatedInput = key;
                reason = $"输入 {key} 不是有限值。";
                return false;
            }

            FormulaRange range = Ranges[key];
            if (!range.Contains(value))
            {
                violatedInput = key;
                reason = $"输入 {key}={value} 超出适用域。";
                return false;
            }
        }

        return true;
    }

    public FormulaDomain Normalize()
    {
        if (Ranges is null)
            return Empty;

        var sorted = new SortedDictionary<string, FormulaRange>(StringComparer.Ordinal);
        foreach (KeyValuePair<string, FormulaRange> pair in Ranges)
            sorted[pair.Key] = pair.Value;

        return new FormulaDomain(sorted, Guard);
    }
}
