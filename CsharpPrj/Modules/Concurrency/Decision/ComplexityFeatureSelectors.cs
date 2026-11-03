using System;
using System.Collections.Generic;

namespace Modules.Concurrency;

/// <summary>
/// 特征选择器实现表（受控词 → 输入取值）：spec 里声明 selector，实现只能在此登记。
/// <para>用例断言 spec.selector 集 ≡ 本表键集（缺失即红）；运行期未登记 selector ⇒ 抛（禁静默 0）。</para>
/// </summary>
public static class ComplexityFeatureSelectors
{
    private static readonly IReadOnlyDictionary<string, Func<ComplexityInput, double>> Table =
        new Dictionary<string, Func<ComplexityInput, double>>(StringComparer.Ordinal)
        {
            ["text_length"] = static input => input.Text?.Length ?? 0,
            ["deliverable_count"] = static input => input.DeliverableCount,
            ["target_kind_count"] = static input => input.TargetKindCount,
            ["node_count"] = static input => input.NodeCount,
            ["cross_module_count"] = static input => input.CrossModuleCount,
            ["sub_agent_count"] = static input => input.SubAgentCount,
        };

    /// <summary>已实现的选择器词集（供 spec 对拍）。</summary>
    public static IEnumerable<string> Keys => Table.Keys;

    /// <summary>取选择器实现；未登记 ⇒ 抛（声明面与实现面失配属配置缺陷，不得静默降级）。</summary>
    public static Func<ComplexityInput, double> Resolve(string selector)
    {
        ArgumentNullException.ThrowIfNull(selector);
        if (!Table.TryGetValue(selector, out var resolve))
            throw new InvalidOperationException(
                $"未登记的特征选择器 '{selector}'：请先在 ComplexityFeatureSelectors 实现，或从 spec 移除该行。");
        return resolve;
    }
}
