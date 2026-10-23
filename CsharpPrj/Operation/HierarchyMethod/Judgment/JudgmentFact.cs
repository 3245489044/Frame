using System;
using System.Collections.Generic;
using Modules.Node.Ports;

namespace Operation.HierarchyMethod.Judgment;

/// <summary>运行期事实（键存在即事实；null 值保留为 ExplicitNull）。</summary>
public sealed record JudgmentFact(IReadOnlyDictionary<string, object?> Values)
{
    public static JudgmentFact Empty { get; } = new(new Dictionary<string, object?>(StringComparer.Ordinal));

    public PortPresence PresenceOf(string key, PresenceSemantics semantics)
    {
        if (string.IsNullOrWhiteSpace(key)) return PortPresence.Absent;
        if (!Values.TryGetValue(key, out var value)) return PortPresence.Absent;
        if (value is null) return semantics.NullAs;
        if (value is string text && string.IsNullOrWhiteSpace(text)) return semantics.BlankAs;
        return PortPresence.Value;
    }

    /// <summary>键集合事实（静态闭合用）：键存在 = Value。</summary>
    public static JudgmentFact FromKeys(IEnumerable<string> keys)
    {
        var values = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (!string.IsNullOrWhiteSpace(key)) values[key] = true;
        }

        return new JudgmentFact(values);
    }
}
