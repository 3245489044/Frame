using System.Globalization;

namespace MathematicalAlgorithms.FormulaFit;

internal static class FormulaFitConditions
{
    private static readonly Comparer<string> ValueComparer = Comparer<string>.Create(CompareValues);

    public static bool TryCollect(
        FormulaFitPlan plan,
        FormulaFitTargetPlan target,
        IReadOnlyList<SweepPoint> usedPoints,
        out IReadOnlyDictionary<string, string> conditions,
        out string? error)
    {
        var collected = new SortedDictionary<string, string>(StringComparer.Ordinal);
        conditions = collected;
        error = null;

        if (plan.Window?.Conditions is not null)
        {
            foreach (KeyValuePair<string, string> pair in plan.Window.Conditions)
                collected[pair.Key] = pair.Value;
        }

        if (target.GroupBy is not null)
        {
            foreach (string pointer in target.GroupBy)
            {
                var values = new SortedSet<string>(ValueComparer);
                foreach (SweepPoint point in usedPoints)
                {
                    if (!point.TryGetString(pointer, out string value, out string? reason))
                    {
                        error = $"分组路径 {pointer} 无法解析：{reason}";
                        return false;
                    }

                    values.Add(NormalizeValue(value));
                }

                if (values.Count == 0)
                {
                    error = $"分组路径 {pointer} 没有可用值。";
                    return false;
                }

                string key = JsonPointer.LastToken(pointer);
                if (collected.ContainsKey(key))
                    key = pointer;

                collected[key] = string.Join(",", values);
            }
        }

        conditions = collected;
        return true;
    }

    private static int CompareValues(string? left, string? right)
    {
        bool leftNumeric = double.TryParse(left, NumberStyles.Float, CultureInfo.InvariantCulture, out double leftValue);
        bool rightNumeric = double.TryParse(right, NumberStyles.Float, CultureInfo.InvariantCulture, out double rightValue);

        if (leftNumeric && rightNumeric)
            return leftValue.CompareTo(rightValue);

        if (leftNumeric)
            return -1;

        if (rightNumeric)
            return 1;

        return string.CompareOrdinal(left, right);
    }

    private static string NormalizeValue(string value)
        => double.TryParse(value, NumberStyles.Float, CultureInfo.InvariantCulture, out double numeric)
            ? numeric.ToString("R", CultureInfo.InvariantCulture)
            : value;
}
