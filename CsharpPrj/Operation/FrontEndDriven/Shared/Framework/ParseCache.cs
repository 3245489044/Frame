#nullable enable

using System;
using System.Collections.Generic;

namespace FrontEndDriven.Shared.Framework;

/// <summary>
/// 值解析缓存（通用框架）：仅缓存解析成功的结果；负结果不缓存（随时可重试），
/// 避免把「格式非法」与「值已失效」混入同一桶。
/// 可注入有效性谓词，在缓存命中时对已缓存值做存活复核（如 Entity 销毁后旧代值失效）。
/// 收敛 EntityIdParser + EntityIdCache 的同构"解析 + 缓存"模式。
/// </summary>
/// <typeparam name="TSource">源（输入）类型。</typeparam>
/// <typeparam name="TValue">解析出的值类型。</typeparam>
public sealed class ParseCache<TSource, TValue>
{
    private readonly Dictionary<TSource, TValue> _cache;
    private readonly TryParseFunc _parser;
    private readonly Func<TValue, bool>? _isValid;

    public delegate bool TryParseFunc(TSource source, out TValue value);

    /// <param name="parser">纯解析函数（可重入、无副作用）。</param>
    /// <param name="isValid">可选有效性谓词：命中缓存时复核已缓存值是否仍有效（失效则按未命中重新解析）。</param>
    /// <param name="comparer">可选键比较器。</param>
    public ParseCache(
        TryParseFunc parser,
        Func<TValue, bool>? isValid = null,
        IEqualityComparer<TSource>? comparer = null)
    {
        _parser = parser ?? throw new ArgumentNullException(nameof(parser));
        _isValid = isValid;
        _cache = comparer == null
            ? new Dictionary<TSource, TValue>()
            : new Dictionary<TSource, TValue>(comparer);
    }

    /// <summary>
    /// 解析并缓存（仅成功结果入缓存）：命中且仍有效 → 返回缓存值；命中但失效 → 重新解析；未命中 → 解析并缓存成功项。
    /// </summary>
    public bool TryParse(TSource source, out TValue value)
    {
        if (source == null)
        {
            value = default!;
            return false;
        }

        if (_cache.TryGetValue(source, out var cached))
        {
            if (_isValid == null || _isValid(cached))
            {
                value = cached;
                return true;
            }
            // 缓存值已失效（如实体销毁）：按未命中重新解析
        }

        if (!_parser(source, out value))
            return false;

        _cache[source] = value;
        return true;
    }

    public void Clear() => _cache.Clear();
}
