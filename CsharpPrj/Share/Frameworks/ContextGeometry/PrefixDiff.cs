namespace Share.ContextGeometry;

/// <summary>段级漂移定位（纯函数）：新增 / 删除 / 内容变化的段键。</summary>
public static class PrefixDiff
{
    /// <summary>返回变化键集合（空 = 无漂移）；<paramref name="previous"/> 为 null ⇒ 无法比对，返回空集。</summary>
    public static IReadOnlyList<string> ChangedKeys(GeometrySnapshot? previous, IEnumerable<PrefixSegment>? current)
    {
        var next = (current ?? Enumerable.Empty<PrefixSegment>()).ToArray();
        if (previous is null) return Array.Empty<string>();

        var before = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var segment in previous.Segments ?? Array.Empty<PrefixSegment>())
            before[segment.Key] = segment.Hash;

        var changed = new List<string>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var segment in next)
        {
            seen.Add(segment.Key);
            if (!before.TryGetValue(segment.Key, out var hash)
                || !string.Equals(hash, segment.Hash, StringComparison.Ordinal))
                changed.Add(segment.Key);
        }

        foreach (var key in before.Keys)
            if (!seen.Contains(key)) changed.Add(key);

        return changed;
    }
}
