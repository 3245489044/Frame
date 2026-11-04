#nullable enable

using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace UiBackend.Html;

/// <summary>
/// 命令属性工厂（通用件）—— 把「单个基座属性（键名 + 值）」按值缓存为只读基座，
/// 各命令取基座 + 追加差异键。消除渲染路径内逐命令 <c>new Dictionary</c> 重复分配。
/// <para>键名语义由调用方声明（如 <c>type</c>）；本类不解释业务含义。</para>
/// </summary>
public static class HtmlCommandAttributes
{
    private static readonly ConcurrentDictionary<string, Dictionary<string, string>> BaseCache =
        new(StringComparer.Ordinal);

    /// <summary>取某值的基座只读副本（首调用构造并缓存；调用方共享，禁止修改返回值）。</summary>
    /// <param name="attributeName">基座属性名（如 <c>type</c>）。</param>
    /// <param name="value">基座属性值（null/空 ⇒ 空串）。</param>
    public static Dictionary<string, string> Base(string attributeName, string? value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(attributeName);
        var name = attributeName.Trim();
        var key = name + "\u0001" + (value ?? string.Empty);
        return BaseCache.GetOrAdd(key, _ => new Dictionary<string, string>(StringComparer.Ordinal)
        {
            [name] = value ?? string.Empty,
        });
    }

    /// <summary>基座 + 追加差异键（返回新实例，缓存不被污染）。</summary>
    public static Dictionary<string, string> For(
        string attributeName,
        string? value,
        params (string Key, string Value)[] extra)
    {
        var map = new Dictionary<string, string>(Base(attributeName, value), StringComparer.Ordinal);
        if (extra is not null)
        {
            foreach (var (key, item) in extra)
            {
                map[key] = item;
            }
        }

        return map;
    }
}
