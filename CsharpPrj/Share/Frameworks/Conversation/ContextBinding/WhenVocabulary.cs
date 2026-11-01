#nullable enable

using System;
using System.Collections.Generic;
using Share.Composition.Text;

namespace Share.Conversation.ContextBinding;

/// <summary>
/// 适用性词表（<c>when=</c>）—— 声明数据 → 谓词的<b>唯一</b>求值点。
/// <para>
/// 词表是中立枚举（不触通用内核纯度门禁），提示词域的语义映射在此完成：
/// <c>always</c>（恒适用）/ <c>continue</c>（仅继续步）/ <c>replan</c>（仅重规划步）。
/// 事实缺失时一律判「适用」（不因未知而抑制产出，避免把观测缺口变成行为变化）。
/// </para>
/// </summary>
public static class WhenVocabulary
{
    /// <summary>恒适用。</summary>
    public const string Always = "always";

    /// <summary>仅继续步适用（首步不适用）。</summary>
    public const string Continue = "continue";

    /// <summary>仅重规划步适用。</summary>
    public const string Replan = "replan";

    /// <summary>上下文事实键：是否首步。</summary>
    public const string FactIsFirstStep = "is_first_step";

    /// <summary>上下文事实键：是否重规划步。</summary>
    public const string FactIsReplan = "is_replan";

    /// <summary>词是否在册（未在册 ⇒ 声明不合格，由门禁报出）。</summary>
    public static bool IsKnown(string? when)
        => string.IsNullOrWhiteSpace(when)
           || string.Equals(when, Always, StringComparison.OrdinalIgnoreCase)
           || string.Equals(when, Continue, StringComparison.OrdinalIgnoreCase)
           || string.Equals(when, Replan, StringComparison.OrdinalIgnoreCase);

    /// <summary>词 → 适用性谓词（未知词按恒适用处理，由门禁另行报出）。</summary>
    public static Func<ISegmentContext, bool> Resolve(string? when)
    {
        if (string.Equals(when, Continue, StringComparison.OrdinalIgnoreCase))
            return static ctx => !FactIsTrue(ctx, FactIsFirstStep);
        if (string.Equals(when, Replan, StringComparison.OrdinalIgnoreCase))
            return static ctx => FactIsTrue(ctx, FactIsReplan);
        return static _ => true;
    }

    private static bool FactIsTrue(ISegmentContext context, string key)
        => context.TryGet(key, out var value) && value is bool flag && flag;
}
