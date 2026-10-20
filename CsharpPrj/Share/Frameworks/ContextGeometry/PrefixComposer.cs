namespace Share.ContextGeometry;

/// <summary>
/// 前缀组合（纯函数）：稳定排序（Band 升序 + 声明序）→ 去重（段键 / 内容键）→ 规范字节 → 摘要。
/// <para>禁：字典序归一、空白归一、运行期重排（顺序即语义 ⇒ 重排 = 缓存归零）。</para>
/// </summary>
public static class PrefixComposer
{
    /// <summary>组合段集合；<paramref name="blocks"/> 非空时同时登记规范字节并返回 <see cref="ComposedPrefix.CanonicalRef"/>。</summary>
    public static ComposedPrefix Compose(IEnumerable<PrefixSegment>? segments, ISharedBlockStore? blocks = null)
    {
        var diagnostics = new List<string>();
        var ordered = new List<PrefixSegment>();
        var seenKeys = new HashSet<string>(StringComparer.Ordinal);
        var seenBlocks = new HashSet<string>(StringComparer.Ordinal);

        if (segments is not null)
        {
            // OrderBy 为稳定排序：同 Band 保持声明序（顺序即语义）。
            foreach (var segment in segments.Where(s => s is not null).OrderBy(s => (int)s.Band))
            {
                if (!CacheBandRules.IsPrefixAllowed(segment.Band))
                {
                    diagnostics.Add($"{GeometryDiagnostics.UnknownBand}:{segment.Key}");
                    continue;
                }

                if (!seenKeys.Add(segment.Key))
                {
                    diagnostics.Add($"{GeometryDiagnostics.DuplicateKey}:{segment.Key}");
                    continue;
                }

                if (!string.IsNullOrWhiteSpace(segment.ContentRef) && !seenBlocks.Add(segment.ContentRef!))
                {
                    diagnostics.Add($"{GeometryDiagnostics.DuplicateBlock}:{segment.ContentRef}");
                    continue;
                }

                ordered.Add(segment);
            }
        }

        var canonical = CanonicalText(ordered);
        var digest = ContentHash.Sha256Hex(canonical);
        string? canonicalRef = null;
        if (blocks is not null)
            canonicalRef = blocks.Put(digest, canonical);

        return new ComposedPrefix(digest, ordered, canonicalRef, diagnostics);
    }

    /// <summary>规范字节（唯一拼接口径）：每段 <c>key|band|hash|contentRef\n</c>。</summary>
    public static string CanonicalText(IEnumerable<PrefixSegment>? segments)
    {
        var sb = new System.Text.StringBuilder();
        if (segments is not null)
        {
            foreach (var segment in segments)
            {
                sb.Append(segment.Key ?? string.Empty).Append('|')
                  .Append(segment.Band.ToString()).Append('|')
                  .Append(segment.Hash ?? string.Empty).Append('|')
                  .Append(segment.ContentRef ?? string.Empty).Append('\n');
            }
        }
        return sb.ToString();
    }
}
