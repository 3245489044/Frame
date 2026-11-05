#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.Composition.Text;

/// <summary>
/// 计划构建唯一实现 —— 声明 × 上下文 → 计划（不拼接、不判动作）。
/// <para>
/// 生成顺序固定：开关过滤 → 适用性（false 直接记 NotApplicable，<b>不调用 Resolve</b>，避免副作用）
/// → 取值 → 按 (Order, 声明序) 稳定排序 → 派生契约 → 计算指纹。
/// </para>
/// </summary>
public static class TextPlanBuilder
{
    /// <summary>构建计划。</summary>
    /// <param name="specs">段声明（注册表视图）。</param>
    /// <param name="context">事实上下文。</param>
    /// <param name="profileKey">档案键（诊断标识，不参与排序）。</param>
    public static TextPlan Build(IEnumerable<TextSpec> specs, ISegmentContext context, string profileKey = "")
    {
        ArgumentNullException.ThrowIfNull(specs);
        ArgumentNullException.ThrowIfNull(context);

        var declared = specs.ToArray();
        var ordered = declared
            .Select((spec, index) => (spec, index))
            .OrderBy(static t => t.spec.Order)
            .ThenBy(static t => t.index)
            .ToArray();

        var blocks = new List<TextBlock>(ordered.Length);
        var lines = new List<string>(ordered.Length);

        foreach (var (spec, _) in ordered)
        {
            TextBlock block;
            if (spec.Toggle is { Length: > 0 } toggle && !context.IsEnabled(toggle))
            {
                block = TextBlock.NotApplicable(spec.Key, "toggle-off:" + toggle, spec.Provider);
            }
            else if (!spec.Applicable(context))
            {
                block = TextBlock.NotApplicable(spec.Key, "not-applicable", spec.Provider);
            }
            else
            {
                block = spec.Resolve(context) with
                {
                    Key = spec.Key,
                    Group = spec.Group,
                    Order = spec.Order,
                    Source = spec.Provider,
                };
            }

            blocks.Add(block);
            lines.Add($"{spec.Key.Value}|{block.State}|{TextDigest.Hash(block.Text)}|{spec.Group}|{spec.Order.Value}");
        }

        return new TextPlan(blocks, TextContract.From(declared), profileKey, TextDigest.HashLines(lines));
    }
}
