
using System;
using System.Collections.Generic;
using Microsoft.CodeAnalysis;

namespace Share.RoslynJson.Generation;

/// <summary>
/// 生成器输入排序：统一按路径（OrdinalIgnoreCase）升序，消除增量生成顺序抖动（diff/缓存稳定性）。
/// </summary>
public static class GeneratorInputOrdering
{
    public static IReadOnlyList<AdditionalText> OrderByPath(IEnumerable<AdditionalText> texts)
    {
        var ordered = new List<AdditionalText>();

        foreach (var text in texts ?? Array.Empty<AdditionalText>())
        {
            if (text is not null) ordered.Add(text);
        }

        ordered.Sort(static (left, right) =>
            string.Compare(left.Path, right.Path, StringComparison.OrdinalIgnoreCase));

        return ordered;
    }
}
