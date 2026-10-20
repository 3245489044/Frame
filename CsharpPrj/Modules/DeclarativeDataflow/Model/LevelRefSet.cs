using System;
using System.Collections.Generic;

namespace Modules.DeclarativeDataflow.Model;

/// <summary>
/// <see cref="LevelRef"/> 集合的归一化（**唯一实现**）—— 保序去重 + 过滤空引用。
/// <para>
/// 判据：<see cref="LevelRef"/> 的**值相等即实体同一性**（层 Id + 作用域 + 实体 Id），
/// 故任意层次实现（内核拓扑聚合 / 宿主注入读取器 / 引用图适配器）不得各自重写同一段去重逻辑。
/// </para>
/// <para>只读纯函数：无副作用、无 IO；输入顺序即输出顺序（去重保留首次出现）。</para>
/// </summary>
public static class LevelRefSet
{
    /// <summary>
    /// 归一化层引用集合：过滤 <see cref="LevelRef.IsEmpty"/>/<c>null</c> 后按值去重，保留首次出现顺序。
    /// </summary>
    /// <param name="facts">原始事实（可空；<c>null</c> / 空集 → 空列表）。</param>
    /// <returns>保序、去重、无空引用的只读列表。</returns>
    public static IReadOnlyList<LevelRef> Normalize(IEnumerable<LevelRef>? facts)
    {
        if (facts is null) return Array.Empty<LevelRef>();

        var results = new List<LevelRef>();
        var seen = new HashSet<LevelRef>();
        foreach (var fact in facts)
        {
            if (fact.IsEmpty || !seen.Add(fact)) continue;
            results.Add(fact);
        }
        return results.Count == 0 ? Array.Empty<LevelRef>() : results;
    }
}
