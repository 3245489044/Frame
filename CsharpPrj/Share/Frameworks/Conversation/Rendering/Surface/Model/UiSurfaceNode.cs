#nullable enable

using System.Collections.Generic;
using Share.Conversation.Presentation;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 载体中立渲染节点（**语义层**）：只承载角色/键/结构面/字段/子节点/态/动作；
/// 不含任何载体名（tag/控件名），也不含已拼好的正文（正文由后端合成）。
/// </summary>
/// <param name="Role">角色（<see cref="SurfaceRoleIds"/>）。</param>
/// <param name="Key">稳定键（差分/折叠单位；禁随机）。</param>
/// <param name="Surface">复用既有结构面（bubble/process/operation/notice/unknown）。</param>
/// <param name="Class">呈现类 id（<c>PresentationDeclaration</c> 的键）。</param>
/// <param name="Fields">结构化字段（有序）。</param>
/// <param name="Children">子节点（有序）。</param>
/// <param name="State">折叠/选中态。</param>
/// <param name="Actions">受控动作（selectTask/toggleFold/copy…）。</param>
public sealed record UiSurfaceNode(
    string Role,
    string Key,
    PresentationSurface Surface,
    string Class,
    IReadOnlyList<UiField> Fields,
    IReadOnlyList<UiSurfaceNode> Children,
    UiNodeState State = UiNodeState.Open,
    IReadOnlyList<string>? Actions = null)
{
    /// <summary>折叠提示（结构事实：open/closed；null ⇒ 载体默认收起）。不参与渲染文本。</summary>
    public string? FoldHint { get; init; }

    /// <summary>折叠种类（结构事实：<c>banner</c> = 段横幅 / <c>row</c> = 行摘要；null ⇒ 旧折叠体）。</summary>
    public string? FoldKind { get; init; }

    /// <summary>摘要截断上限（字符数；0 = 不截断；只作用于折叠摘要首行）。</summary>
    public int SummaryMaxChars { get; init; }

    /// <summary>声明样式 token（视觉区分位；null ⇒ 仅角色/类主题）。</summary>
    public string? Style { get; init; }

    /// <summary>分类标识（<c>PresentationDeclaration</c> 的分类键；null ⇒ 非分类段/未声明）。</summary>
    public string? Category { get; init; }

    /// <summary>结构事实（进载体 <c>data-fact-*</c>；不参与可见字段计数）。</summary>
    public IReadOnlyDictionary<string, string>? Facts { get; init; }

    /// <summary>叶子节点（无子）。</summary>
    public bool IsLeaf => Children.Count == 0;
}
