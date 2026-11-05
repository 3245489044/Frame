using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace Share.PortContexts;

/// <summary>装配器辅助（供数加载/归一/字段族过滤；唯一实现）。</summary>
public sealed partial class PortContextAssembler
{
    private async Task<Dictionary<string, IReadOnlyList<PortContextField>>?> LoadStateAsync(
        string containerKey, IReadOnlyList<string> memberKeys, CancellationToken ct)
    {
        if (_states is null) return null;
        try
        {
            var rows = await _states.GetStateAsync(containerKey, memberKeys, ct).ConfigureAwait(false);
            var map = new Dictionary<string, IReadOnlyList<PortContextField>>(StringComparer.Ordinal);
            foreach (var row in rows ?? Array.Empty<PortContextStateRow>())
            {
                if (row is null || string.IsNullOrWhiteSpace(row.MemberKey)) continue;
                map[row.MemberKey.Trim()] = row.Fields ?? Array.Empty<PortContextField>();
            }

            return map;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            return null;
        }
    }

    private async Task<Dictionary<string, IReadOnlyList<PortContextWindowItem>>> LoadWindowAsync(
        string containerKey, IReadOnlyList<string> memberKeys, int take, CancellationToken ct)
    {
        var map = new Dictionary<string, IReadOnlyList<PortContextWindowItem>>(StringComparer.Ordinal);
        if (_windows is null) return map;

        try
        {
            var items = await _windows.GetWindowAsync(containerKey, memberKeys, take, ct).ConfigureAwait(false);
            foreach (var item in items ?? Array.Empty<PortContextWindowItem>())
            {
                if (item is null || string.IsNullOrWhiteSpace(item.Owner)) continue;
                var owner = item.Owner.Trim();
                map[owner] = map.TryGetValue(owner, out var existing)
                    ? existing.Concat(new[] { item }).ToArray()
                    : new[] { item };
            }
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch
        {
            map.Clear();
        }

        return map;
    }

    private static IReadOnlyList<string> Normalize(IReadOnlyList<string> keys)
    {
        var result = new List<string>(keys.Count);
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in keys)
        {
            if (string.IsNullOrWhiteSpace(key)) continue;
            var trimmed = key.Trim();
            if (seen.Add(trimmed)) result.Add(trimmed);
        }

        return result;
    }

    private static IReadOnlyList<PortContextField> Filter(
        PortContextSpec spec, IReadOnlyList<PortContextField>? fields)
    {
        if (fields is not { Count: > 0 }) return Array.Empty<PortContextField>();
        return fields
            .Where(field => field is not null && Allowed(spec, field.Class))
            .ToArray();
    }

    private static bool Allowed(PortContextSpec spec, string fieldClass)
        => spec.FieldClasses is not { Count: > 0 }
           || spec.FieldClasses.Contains(fieldClass, StringComparer.Ordinal);
}
