#nullable enable

using System;
using System.Collections.Generic;
using Framework.Tree;
using UiBackend.Abstractions;

namespace UiBackend.Html;

/// <summary>
/// <see cref="UiNode"/> 树遍历单一入口（FrameworkTree 适配）——<see cref="UiNode.Children"/> 可空，
/// 统一收敛为可枚举子节点；供键守卫（<see cref="HtmlKeyGuard"/>）/键契约（<see cref="HtmlKeyContract"/>）共用同一遍历器，
/// 消除同命名空间下多段平行手写递归（对齐 axioms:Strategy.SingleImplementation）。
/// </summary>
internal static class UiNodeTrees
{
    /// <summary>UiNode 通用遍历器（默认 PreOrder，序等价于原手写前序递归）。</summary>
    internal static readonly TreeWalker<UiNode> Walker =
        new(n => (IEnumerable<UiNode>?)n.Children ?? Array.Empty<UiNode>());
}
