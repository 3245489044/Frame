using System.Globalization;
using System.Text.Json;

namespace MathematicalAlgorithms.FormulaFit;

public sealed partial record SweepPoint
{
    private static bool TryReadNumber(
        JsonElement element,
        string aggregate,
        out double value,
        out string? error)
    {
        value = 0;
        error = null;

        switch (element.ValueKind)
        {
            case JsonValueKind.Number:
                return element.TryGetDouble(out value) && double.IsFinite(value);

            case JsonValueKind.String:
                if (double.TryParse(element.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value)
                    && double.IsFinite(value))
                {
                    return true;
                }

                error = $"字符串不是有限数值：{element.GetString()}";
                return false;

            case JsonValueKind.Array:
                return TryAggregate(element.EnumerateArray(), aggregate, out value, out error);

            case JsonValueKind.Object:
                if (element.TryGetProperty("value", out JsonElement valueElement))
                    return TryReadNumber(valueElement, aggregate, out value, out error);

                if (element.TryGetProperty("runs", out JsonElement runsElement))
                    return TryReadNumber(runsElement, aggregate, out value, out error);

                error = "对象既无 value 也无 runs。";
                return false;

            default:
                error = $"不支持的元素类型：{element.ValueKind}";
                return false;
        }
    }

    private static bool TryAggregate(
        JsonElement.ArrayEnumerator items,
        string aggregate,
        out double value,
        out string? error)
    {
        value = 0;
        error = null;

        var values = new List<double>();
        foreach (JsonElement item in items)
        {
            if (!TryReadNumber(item, "first", out double number, out string? reason))
            {
                error = reason;
                return false;
            }

            values.Add(number);
        }

        if (values.Count == 0)
        {
            error = "数组为空。";
            return false;
        }

        switch (aggregate)
        {
            case "first":
                value = values[0];
                break;

            case "last":
                value = values[^1];
                break;

            case "min":
                value = values.Min();
                break;

            case "max":
                value = values.Max();
                break;

            case "mean":
                value = values.Average();
                break;

            case "median":
                value = Median(values);
                break;

            default:
                error = $"未知聚合方式：{aggregate}";
                return false;
        }

        return double.IsFinite(value);
    }

    private static double Median(List<double> values)
    {
        values.Sort();
        int middle = values.Count / 2;
        return values.Count % 2 == 1
            ? values[middle]
            : (values[middle - 1] + values[middle]) / 2.0;
    }
}
