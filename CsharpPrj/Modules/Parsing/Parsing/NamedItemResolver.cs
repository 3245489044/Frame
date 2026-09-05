using System;
using System.Collections.Generic;
using System.Linq;

namespace Modules.Parsing.Parsing;

/// <summary>
/// 具名解析器 —— 从一组候选里按键（名称/提示）精确匹配，未命中/空键回退首个；空集返回 null。
/// 通用"名称→候选"选择策略，供 provider / executor / strategy 等具名注册解析复用。
/// </summary>
public static class NamedItemResolver
{
    /// <summary>按 nameOf(item)==key 匹配（忽略大小写，优先），否则回退首个；空集返回 null。</summary>
    public static T? Resolve<T>(IReadOnlyList<T> items, Func<T, string> nameOf, string? key)
        where T : class
    {
        if (!string.IsNullOrWhiteSpace(key))
        {
            var byName = items.FirstOrDefault(e => string.Equals(nameOf(e), key, StringComparison.OrdinalIgnoreCase));
            if (byName != null) return byName;
        }
        return items.Count > 0 ? items[0] : null;
    }

    /// <summary>
    /// 结构化解析：优先按 hint（提示名）精确匹配；未命中/空 hint 回退按 fallbackKey（回退键）匹配；
    /// fallbackKey 亦为空则回退首个；空集返回 null。对应"描述符 {hint, fallbackKey} → 候选"的选择策略。
    /// </summary>
    public static T? ResolveByHint<T>(IReadOnlyList<T> items, Func<T, string> nameOf, string? hint, string? fallbackKey)
        where T : class
    {
        if (!string.IsNullOrWhiteSpace(hint))
        {
            var byHint = Resolve(items, nameOf, hint);
            if (byHint != null) return byHint;
        }
        return Resolve(items, nameOf, fallbackKey);
    }
}
