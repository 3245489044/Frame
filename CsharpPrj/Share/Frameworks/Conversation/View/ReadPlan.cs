using System;
using System.Collections.Generic;

namespace Share.Conversation.View;

/// <summary>
/// 读面终答状态（四态）。四态均为可见出口：<see cref="Unknown"/> ≠ <see cref="NoAnswer"/>（未知 ≠ 没有）。
/// </summary>
public enum AnswerStatus
{
    /// <summary>存在合法终答行（<c>terminal=true</c> 且权威级允许）。</summary>
    Answered = 0,

    /// <summary>有轮事实但无合格终答（显式状态，禁空白）。</summary>
    NoAnswer = 1,

    /// <summary>只有过程帧、无对话锚点（过程行不铺成气泡）。</summary>
    ProcessOnly = 2,

    /// <summary>源不可用 / 事实缺失（不是「没有」）。</summary>
    Unknown = 3,
}

/// <summary>一条读源（<see cref="ReadPlan"/> 的 sources 项）。</summary>
/// <param name="Domain">端口域键（对应一个 <see cref="IFrameSource"/>）。</param>
/// <param name="Selector">域内选择器（受控词；空 = 域缺省）。</param>
/// <param name="Precedence">读序（小者先；同值按声明序）。</param>
/// <param name="AnchorCapable">该源能否提供气泡锚点。</param>
/// <param name="Surface">该源呈现面（如 <c>process</c>；null = 不限定）。</param>
/// <param name="Facts">该源声明的事实字段名。</param>
/// <param name="Role">读源角色（<c>spine</c> = 会话脊梁，首非空即用；<c>supplement</c> = 合并补位）。</param>
public sealed record ReadSource(
    string Domain,
    string Selector,
    int Precedence,
    bool AnchorCapable,
    string? Surface,
    IReadOnlyList<string> Facts,
    string Role = ReadSourceRoles.Spine);

/// <summary>读源角色受控词（声明面 <c>read-paths.paths[].role</c> 的唯一取值域）。</summary>
public static class ReadSourceRoles
{
    /// <summary>会话脊梁：按 precedence 首非空即采用。</summary>
    public const string Spine = "spine";

    /// <summary>补位源：与脊梁结果合并（去重/归位），不参与首非空短路。</summary>
    public const string Supplement = "supplement";
}

/// <summary>终答规则（编译期合并 <c>answer-facts.json</c>）。</summary>
/// <param name="Authority">可投影的终答权威级。</param>
/// <param name="Eligible">可成为终答投影目标的帧类别。</param>
/// <param name="Select">选择规则（顺序即优先级）。</param>
/// <param name="Absent">无合格终答时的显式状态。</param>
/// <param name="TerminalPresentation">终答行的呈现身份（<c>presentation.json.facts.terminal</c>）。</param>
public sealed record AnswerRule(
    IReadOnlyList<string> Authority,
    IReadOnlyList<string> Eligible,
    IReadOnlyList<string> Select,
    AnswerStatus Absent,
    string TerminalPresentation)
{
    /// <summary>空规则（未编译；调用方须显式可见化）。</summary>
    public static readonly AnswerRule Empty =
        new(Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), AnswerStatus.Unknown, string.Empty);
}

/// <summary>单视图读计划（主键 = 视图键）。</summary>
/// <param name="View">视图键。</param>
/// <param name="Sources">读源（按 precedence）。</param>
/// <param name="Answer">终答规则。</param>
/// <param name="Fingerprint">内容指纹。</param>
public sealed record ReadPlan(
    string View,
    IReadOnlyList<ReadSource> Sources,
    AnswerRule Answer,
    string Fingerprint);

/// <summary>读计划表（编译产物；UI 与装配器只读本表取读序）。</summary>
/// <param name="Plans">视图 → 读计划。</param>
/// <param name="Fingerprint">内容指纹。</param>
public sealed record ReadPlanTable(IReadOnlyDictionary<string, ReadPlan> Plans, string Fingerprint)
{
    /// <summary>空表。</summary>
    public static readonly ReadPlanTable Empty =
        new(new Dictionary<string, ReadPlan>(StringComparer.Ordinal), string.Empty);

    /// <summary>按视图取计划（未声明 ⇒ false）。</summary>
    /// <param name="view">视图键。</param>
    /// <param name="plan">读计划（未命中为 null）。</param>
    public bool TryGet(string? view, out ReadPlan? plan)
    {
        plan = null;
        return !string.IsNullOrWhiteSpace(view) && Plans.TryGetValue(view!, out plan);
    }

    /// <summary>按视图取计划；未声明 ⇒ 抛。</summary>
    /// <param name="view">视图键。</param>
    public ReadPlan Require(string view)
        => TryGet(view, out var plan)
            ? plan!
            : throw new InvalidOperationException($"读计划未声明：{view}（ReadPlan）");
}
