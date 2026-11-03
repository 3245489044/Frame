using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;

namespace Usharp.Semantics;

/// <summary>
/// 编译宿主（**唯一编译入口**）：闭包 + 包集 → 不可变编译快照。
/// <para>
/// 缓存键 = 闭包指纹 ∧ 包集身份；同一进程内同闭包只编译一次（<see cref="CompileMisses"/>
/// 即真实编译次数，取代被误读的 <c>UsharpCompilation.CompileCount</c> 作为门禁判据）。
/// </para>
/// <para>线程安全：<see cref="ConcurrentDictionary{TKey,TValue}"/> + <see cref="Lazy{T}"/> 单次求值。</para>
/// </summary>
public sealed class UsharpCompilationHost
{
    /// <summary>进程级默认宿主（组合根/声明消费方共用）。</summary>
    public static UsharpCompilationHost Shared { get; } = new();

    private readonly ConcurrentDictionary<string, Lazy<UsharpCompilation>> _cache =
        new(StringComparer.Ordinal);
    private int _misses;
    private int _hits;

    /// <summary>真实编译次数（缓存未命中数）。</summary>
    public int CompileMisses => Volatile.Read(ref _misses);

    /// <summary>缓存命中次数。</summary>
    public int CacheHits => Volatile.Read(ref _hits);

    /// <summary>取编译快照（未命中才编译）。</summary>
    /// <param name="closure">声明闭包。</param>
    /// <param name="packs">领域包集（null/空 ⇒ 默认包，零回归）。</param>
    public UsharpCompilation Resolve(DeclarationClosure closure, IReadOnlyList<IUsharpDomainPack>? packs = null)
    {
        ArgumentNullException.ThrowIfNull(closure);

        var key = closure.Fingerprint + "|" + PackKey(packs);
        var lazy = _cache.GetOrAdd(key, _ =>
        {
            Interlocked.Increment(ref _misses);
            return new Lazy<UsharpCompilation>(
                () => UsharpCompilation.CompileSourcesRaw(closure.Sources, packs),
                LazyThreadSafetyMode.ExecutionAndPublication);
        });

        if (lazy.IsValueCreated) Interlocked.Increment(ref _hits);
        return lazy.Value;
    }

    /// <summary>清空缓存（测试 / 长驻进程显式失效）。</summary>
    public void Clear()
    {
        _cache.Clear();
        Volatile.Write(ref _misses, 0);
        Volatile.Write(ref _hits, 0);
    }

    private static string PackKey(IReadOnlyList<IUsharpDomainPack>? packs)
        => packs is not { Count: > 0 }
            ? "default"
            // 包实例身份参与缓存键：同类型不同配置（如 knownTracks）不得共用编译快照。
            : string.Join(",", packs.Select(p => p is null
                ? "?"
                : $"{p.GetType().FullName}#{RuntimeHelpers.GetHashCode(p)}"));
}
