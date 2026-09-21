
using System;
using System.Collections.Generic;
using System.Linq;

namespace Plate.DslMethod.Parsing;

/// <summary>
/// 前缀作用域集：一个 DSL 领域包（或框架核心）注册的行前缀集合。
/// <para>
/// 作用域化的目的：<b>各包前缀互不可见</b> —— 解析时只识别本集合内的前缀，
/// 避免不同领域的语言面互相污染（如 Usharp 的 <c>[Space]</c> 与叙事包的 <c>[Module]</c> 互不干扰）。
/// </para>
/// <para>按前缀长度降序预排（最长优先：<c>[AX]</c> vs <c>[AX-CORE]</c>），一次性排序避免逐行重排。</para>
/// </summary>
public sealed class DslPrefixSet
{
    public DslPrefixSet(string id, IEnumerable<DslPrefixDef> defs)
    {
        if (defs is null) throw new ArgumentNullException(nameof(defs));
        Id = id ?? string.Empty;
        OrderedPrefixes = defs.OrderByDescending(p => p.Prefix.Length).ToArray();
    }

    /// <summary>作用域标识（包 id，如 <c>core</c> / <c>usharp</c> / <c>nmd</c>）。</summary>
    public string Id { get; }

    /// <summary>本作用域内的前缀（长度降序，最长优先匹配）。</summary>
    public IReadOnlyList<DslPrefixDef> OrderedPrefixes { get; }

    /// <summary>按前缀精确查本作用域条目（未注册返回 null）。</summary>
    public DslPrefixDef? Find(string prefix)
    {
        foreach (var p in OrderedPrefixes)
            if (string.Equals(p.Prefix, prefix, StringComparison.Ordinal)) return p;
        return null;
    }
}
