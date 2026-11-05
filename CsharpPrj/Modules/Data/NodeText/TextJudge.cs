#nullable enable

using System;
using System.Collections.Generic;

namespace Share.Composition.Text;

/// <summary>
/// 契约判定唯一实现 —— 计划 × 契约 → 结论。
/// <para>
/// 四类输入同一结论：键缺失（适用 ⇒ 未知/缺失；不适用 ⇒ 通过）、显式 null（⇒ 空，由 AllowEmpty 裁决）、
/// 空白串（口径由 <paramref name="blankAs"/> 决定）、非空文本（⇒ 通过）。
/// </para>
/// <para>别名派生：规范键未成立而某别名有值 ⇒ 记 <see cref="TextVerdict.Derived"/> 并放行（不产第二套归一逻辑）。</para>
/// </summary>
public static class TextJudge
{
    /// <summary>判定（<paramref name="contract"/> 缺省取计划自带契约）。</summary>
    /// <param name="plan">计划。</param>
    /// <param name="context">事实上下文（别名派生用）。</param>
    /// <param name="contract">契约（null → plan.Contract）。</param>
    /// <param name="blankAs">空白串口径（契约域缺省 Value；前置门域传 Absent）。</param>
    public static TextVerdict Judge(
        TextPlan plan,
        ISegmentContext context,
        TextContract? contract = null,
        BlankSemantics blankAs = BlankSemantics.Value)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);
        contract ??= plan.Contract;

        var missing = new List<TextKey>();
        var unknown = new List<TextKey>();
        var notApplicable = new List<TextKey>();
        var derived = new List<TextKey>();
        var reasons = new List<string>();

        foreach (var port in contract.Ports)
        {
            var state = plan.StateOf(port.Key);

            if (state == TextState.NotApplicable)
            {
                // 声明判「本步适用」，实现却判不适用 ⇒ 声明/实现不一致，按缺失可见化。
                if (port.Requirement == SegmentRequirement.Required && port.Applicable(context))
                {
                    missing.Add(port.Key);
                    reasons.Add($"{port.Key.Value}:声明要求适用但实现判不适用");
                }
                else
                {
                    notApplicable.Add(port.Key);
                }
                continue;
            }

            if (state == TextState.Known) continue;

            if (TryAlias(port, context, blankAs, out var source))
            {
                derived.Add(port.Key);
                reasons.Add($"{port.Key.Value}:derived-from={source}");
                continue;
            }

            if (state == TextState.Empty && port.AllowEmpty) continue;

            if (port.Requirement == SegmentRequirement.Required)
            {
                missing.Add(port.Key);
                reasons.Add($"{port.Key.Value}:{state.ToString().ToLowerInvariant()}");
            }
            else if (state == TextState.Unknown)
            {
                unknown.Add(port.Key);
                reasons.Add($"{port.Key.Value}:unknown");
            }
            // 可选 + 显式空（不可空）⇒ 视为已知为空，不阻断也不可见化未知（对齐契约域既有口径）。
        }

        var status = missing.Count > 0
            ? TextVerdictStatus.Blocked
            : unknown.Count > 0 ? TextVerdictStatus.Degraded : TextVerdictStatus.Complete;

        return new TextVerdict(status, missing, unknown, notApplicable, derived, reasons);
    }

    private static bool TryAlias(PortSpec port, ISegmentContext context, BlankSemantics blankAs, out string source)
    {
        foreach (var alias in port.Aliases)
        {
            if (string.IsNullOrWhiteSpace(alias)) continue;
            if (TextStateMap.Map(context.TryGet(alias, out var value), value, blankAs) == TextState.Known)
            {
                source = alias;
                return true;
            }
        }
        source = string.Empty;
        return false;
    }
}
