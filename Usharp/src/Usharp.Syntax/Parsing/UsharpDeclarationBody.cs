namespace Usharp.Syntax;

/// <summary>声明体解析结果（头部字段 ∪ 续行字段 + 不可解析行）。</summary>
/// <param name="Fields">合并后的字段袋（续行覆盖同名头字段）。</param>
/// <param name="UnparsedLines">无法归位的原始行（已 Trim；禁止静默丢弃）。</param>
public sealed record UsharpDeclarationBodyResult(
    IReadOnlyDictionary<string, string> Fields,
    IReadOnlyList<string> UnparsedLines);

/// <summary>
/// 「声明头 + 续行」的统一解析入口：注释/空行跳过，其余行必须落入字段或 <c>UnparsedLines</c>。
/// <para>
/// 不变量：<c>UnparsedLines + 字段数 == 有效行数</c>（注释/空行不计）——
/// 供 <c>verify</c> 审计与 D-1/D-2 收敛使用。
/// </para>
/// </summary>
public static class UsharpDeclarationBody
{
    /// <summary>解析声明体。</summary>
    /// <param name="headerParts">头部已切分段（含 id 段）。</param>
    /// <param name="bodyLines">续行原文（可为 null）。</param>
    /// <param name="skip">头部跳过的前导段数（缺省 1 = 跳过声明 id）。</param>
    /// <param name="comparer">键比较器。</param>
    public static UsharpDeclarationBodyResult Parse(
        IReadOnlyList<string> headerParts,
        IEnumerable<string>? bodyLines = null,
        int skip = 1,
        StringComparer? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(headerParts);

        var effective = comparer ?? StringComparer.OrdinalIgnoreCase;
        var bag = StrictKeyValueArguments.Parse(headerParts, skip, effective);
        var fields = new Dictionary<string, string>(bag.Fields, effective);
        var unparsed = new List<string>(bag.Unparsed);

        foreach (var raw in bodyLines ?? Array.Empty<string>())
        {
            var line = (raw ?? string.Empty).Trim();
            if (line.Length == 0) continue;
            if (line[0] == '#') continue;

            var eq = line.IndexOf('=');
            if (eq <= 0)
            {
                unparsed.Add(line);
                continue;
            }

            var key = line[..eq].Trim();
            if (key.Length == 0)
            {
                unparsed.Add(line);
                continue;
            }

            fields[key] = line[(eq + 1)..].Trim();
        }

        return new UsharpDeclarationBodyResult(fields, unparsed);
    }

    /// <summary>解析一段文本块（头部行为首行，其余为续行）。</summary>
    /// <param name="lines">原始行集合（首行 = 声明头）。</param>
    /// <param name="marker">声明前缀（用于剥离首段）。</param>
    /// <param name="comparer">键比较器。</param>
    public static UsharpDeclarationBodyResult ParseBlock(
        IEnumerable<string> lines, string marker, StringComparer? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(lines);
        ArgumentNullException.ThrowIfNull(marker);

        var all = lines.ToList();
        if (all.Count == 0) return new UsharpDeclarationBodyResult(
            new Dictionary<string, string>(comparer ?? StringComparer.OrdinalIgnoreCase),
            Array.Empty<string>());

        var head = all[0].Trim();
        var body = all.Skip(1);
        if (!head.StartsWith(marker, StringComparison.Ordinal))
            return Parse(Array.Empty<string>(), all, 0, comparer);

        var rest = head[marker.Length..].Trim();
        var parts = rest.Split('|', StringSplitOptions.TrimEntries);
        return Parse(parts, body, 1, comparer);
    }
}
