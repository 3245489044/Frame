using System;
using System.Collections.Generic;

namespace Share.Conversation.View;

/// <summary>
/// 视图行（UI 唯一可读身份 = <see cref="PresentationClass"/>；渲染层禁再读 channel/role/terminal）。
/// </summary>
/// <param name="Key">稳定行键（源域 + 轮次 + 序 + kind；可审计）。</param>
/// <param name="Kind">帧类别。</param>
/// <param name="PresentationClass">呈现类（→ 模板查表键）。</param>
/// <param name="Text">正文。</param>
/// <param name="Round">轮次（0 = 会话级）。</param>
/// <param name="Attach">附属行（如下拉栏承载的过程行）。</param>
/// <param name="FoldHint">折叠提示（<c>data-*</c> 输出口径）。</param>
public sealed record ViewRow(
    string Key,
    string Kind,
    string PresentationClass,
    string Text,
    int Round,
    IReadOnlyList<ViewRow> Attach,
    string FoldHint);

/// <summary>
/// 视图快照（UI 唯一输入契约）：装配器一次产出，UI 只渲染。
/// </summary>
/// <param name="View">视图键。</param>
/// <param name="Answer">终答状态（四态可见）。</param>
/// <param name="Rows">行（按轮次/源序排列）。</param>
/// <param name="Meta">诊断元数据（源可用性/指纹）。</param>
/// <param name="PlanFingerprint">所用读计划指纹。</param>
public sealed record ViewSnapshot(
    string View,
    AnswerStatus Answer,
    IReadOnlyList<ViewRow> Rows,
    IReadOnlyDictionary<string, string> Meta,
    string PlanFingerprint)
{
    /// <summary>未知态快照（源/计划缺失；<see cref="AnswerStatus.Unknown"/> 与 <see cref="AnswerStatus.NoAnswer"/> 区分）。</summary>
    /// <param name="view">视图键。</param>
    /// <param name="reason">不可用原因（可见化）。</param>
    public static ViewSnapshot Unknown(string view, string reason)
        => new(
            view,
            AnswerStatus.Unknown,
            Array.Empty<ViewRow>(),
            new Dictionary<string, string>(StringComparer.Ordinal) { ["reason"] = reason },
            string.Empty);
}
