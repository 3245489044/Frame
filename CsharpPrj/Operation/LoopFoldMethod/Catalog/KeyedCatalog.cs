using System;
using System.Collections.Generic;
using System.Linq;

namespace Operation.LoopFoldMethod.Catalog;

/// <summary>
/// **域键目录**（通用；表驱动）—— 行式描述集（一行一域）+ 键查找 / 必取。
/// <para>
/// 差异参数 = 描述类型 + 键选择器；「新增一域 = 增一行」的装配纪律由此单点承载。
/// <see cref="Require"/> 未命中即抛（装配期显式失败，不给静默兜底）；<see cref="TryGet"/> 未命中返回 false（探路用）。
/// </para>
/// </summary>
/// <typeparam name="TDescriptor">域描述类型（自含差异参数的数据行）。</typeparam>
public sealed class KeyedCatalog<TDescriptor>
{
    private readonly IReadOnlyList<TDescriptor> _all;
    private readonly Dictionary<string, TDescriptor> _byKey;

    /// <param name="all">全部描述（声明序即装配序）。</param>
    /// <param name="keyOf">描述 → 域键（空键条目不入索引）。</param>
    public KeyedCatalog(IEnumerable<TDescriptor> all, Func<TDescriptor, string> keyOf)
    {
        ArgumentNullException.ThrowIfNull(all);
        ArgumentNullException.ThrowIfNull(keyOf);
        _all = all.ToArray();
        _byKey = new Dictionary<string, TDescriptor>(StringComparer.Ordinal);
        foreach (var descriptor in _all)
        {
            var key = keyOf(descriptor);
            if (!string.IsNullOrWhiteSpace(key))
            {
                _byKey[key] = descriptor;
            }
        }
    }

    /// <summary>全部描述（声明序即装配序）。</summary>
    public IReadOnlyList<TDescriptor> All => _all;

    /// <summary>按键取描述（未注册 ⇒ false，可见化由调用方负责）。</summary>
    /// <param name="key">域键。</param>
    /// <param name="descriptor">描述（未命中为默认值）。</param>
    public bool TryGet(string? key, out TDescriptor descriptor)
    {
        if (!string.IsNullOrWhiteSpace(key) && _byKey.TryGetValue(key, out var found))
        {
            descriptor = found;
            return true;
        }

        descriptor = default!;
        return false;
    }

    /// <summary>按键取描述；未注册 ⇒ 抛（装配期显式失败）。</summary>
    /// <param name="key">域键。</param>
    public TDescriptor Require(string key)
        => TryGet(key, out var descriptor)
            ? descriptor
            : throw new ArgumentException($"未注册的键 '{key}'", nameof(key));
}
