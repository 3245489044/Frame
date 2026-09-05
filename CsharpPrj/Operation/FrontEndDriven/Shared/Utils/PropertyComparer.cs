#nullable enable

using System.Collections.Generic;
using System.Linq;

namespace FrontEndDriven.Shared.Utils;

public static class PropertyComparer
{
    public static bool Equal(Dictionary<string, object?>? a, Dictionary<string, object?>? b)
    {
        var aToB = GetChangedProperties(a, b);
        var bToA = GetChangedProperties(b, a);
        return aToB.Count == 0 && bToA.Count == 0;
    }

    public static Dictionary<string, object?> GetChangedProperties(
        Dictionary<string, object?>? current,
        Dictionary<string, object?>? target)
    {
        var changed = new Dictionary<string, object?>();

        if (current == null && target == null) return changed;
        if (target == null) return changed;

        if (current == null)
        {
            foreach (var (key, value) in target)
                changed[key] = value;
            return changed;
        }

        foreach (var (key, targetValue) in target)
        {
            if (current.TryGetValue(key, out var currentValue))
            {
                if (!Equals(currentValue, targetValue))
                {
                    changed[key] = targetValue;
                }
            }
            else
            {
                changed[key] = targetValue;
            }
        }

        return changed;
    }
}