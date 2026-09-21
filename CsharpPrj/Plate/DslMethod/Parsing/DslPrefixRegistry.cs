
using System;
using System.Collections.Generic;
using System.Linq;

namespace Plate.DslMethod.Parsing;

/// <summary>
/// 前缀作用域注册中心：领域包在此登记自己的 <see cref="DslPrefixSet"/>。
/// <para>
/// 框架核心（<see cref="Core"/>）只含通用 DSL 前缀，<b>不含任何领域前缀</b>；
/// 领域前缀由各包在自身程序集内声明并注册（如 <c>Usharp.Syntax</c> 注册 <c>usharp</c>）。
/// </para>
/// <para>注册/读取加锁，支持多包并发登记；同一 id 后注册覆盖先注册（幂等重建）。</para>
/// </summary>
public static class DslPrefixRegistry
{
    private static readonly object Gate = new object();

    private static readonly Dictionary<string, DslPrefixSet> Packs =
        new Dictionary<string, DslPrefixSet>(StringComparer.Ordinal);

    /// <summary>框架核心前缀集（通用 DSL 词汇，无领域前缀；<see cref="DslParser"/> 的缺省作用域）。</summary>
    public static DslPrefixSet Core { get; } = new DslPrefixSet("core", DslConstants.Prefixes);

    /// <summary>登记一个领域包的前缀作用域。</summary>
    public static void Register(DslPrefixSet set)
    {
        if (set is null) throw new ArgumentNullException(nameof(set));
        lock (Gate) Packs[set.Id] = set;
    }

    /// <summary>按包 id 取前缀作用域（未注册返回 false）。</summary>
    public static bool TryGet(string packId, out DslPrefixSet set)
    {
        lock (Gate) return Packs.TryGetValue(packId ?? string.Empty, out set!);
    }

    /// <summary>已登记的全部领域包前缀作用域（不含 <see cref="Core"/>）。</summary>
    public static IReadOnlyList<DslPrefixSet> Registered
    {
        get { lock (Gate) return Packs.Values.ToArray(); }
    }
}
