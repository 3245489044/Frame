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
    /// <summary>叶子节点（无子）。</summary>
    public bool IsLeaf => Children.Count == 0;
}
