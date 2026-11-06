using System;
using System.Collections.Generic;

namespace Modules.Parsing.Keys;

/// <summary>
/// 宽松 <c>k=v</c> 词袋解析结果：可解析字段 + **不可解析段** + 重复键（禁静默丢弃）。
/// <para>与键文法（<see cref="KeyValues"/>）不同：本结果不绑定任何文法，服务 DSL 行的 <c>|</c> 分段词袋。</para>
/// </summary>
/// <param name="Fields">可解析的 <c>k=v</c> 字段（后者覆盖前者，重复键另列）。</param>
/// <param name="Unparsed">无法解析的原始段（已 Trim）。</param>
/// <param name="DuplicateKeys">重复出现的键（声明序，可重复）。</param>
public sealed record KeyValueParseResult(
    IReadOnlyDictionary<string, string> Fields,
    IReadOnlyList<string> Unparsed,
    IReadOnlyList<string> DuplicateKeys);

/// <summary>
/// 宽松 <c>k=v</c> 词袋的**单一实现**：保留坏段与重复键，供语法层 / 闸A / 文档编解码共用。
/// </summary>
public static class StrictKeyValueArguments
{
    /// <summary>解析字段段集合。</summary>
    /// <param name="parts">已切分的段（通常按 <c>|</c>）。</param>
    /// <param name="skip">跳过的前导段数（首段通常是声明 id）。</param>
    /// <param name="comparer">键比较器（缺省 <see cref="StringComparer.OrdinalIgnoreCase"/>）。</param>
    public static KeyValueParseResult Parse(
        IEnumerable<string> parts, int skip = 0, StringComparer? comparer = null)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var fields = new Dictionary<string, string>(comparer ?? StringComparer.OrdinalIgnoreCase);
        var unparsed = new List<string>();
        var duplicates = new List<string>();

        var index = 0;
        foreach (var raw in parts)
        {
            if (index++ < skip) continue;

            var part = (raw ?? string.Empty).Trim();
            if (part.Length == 0)
            {
                unparsed.Add(part);
                continue;
            }

            var eq = part.IndexOf('=');
            if (eq <= 0)
            {
                unparsed.Add(part);
                continue;
            }

            var key = part[..eq].Trim();
            if (key.Length == 0)
            {
                unparsed.Add(part);
                continue;
            }

            if (fields.ContainsKey(key)) duplicates.Add(key);
            fields[key] = part[(eq + 1)..].Trim();
        }

        return new KeyValueParseResult(fields, unparsed, duplicates);
    }
}
