#nullable enable

using UiBackend.Abstractions;

namespace FrontEndDriven.HtmlViewerBridge;

/// <summary>
/// 会话渲染产物（**中立节点树**，非 HTML 串）：<paramref name="Key"/> = 稳定段键（流原语差分单位），
/// <paramref name="Root"/> = 该段节点根（片段根：<see cref="UiNode.Tag"/> 空串 + 子节点）。
/// <para>字符串只在端口边界出现一次（<see cref="FragmentSerializer"/>），渲染链内部不再造串/解串。</para>
/// </summary>
public sealed record ConversationRenderFragment(string Key, UiNode Root);
