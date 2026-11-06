#nullable enable

using System;
using System.Collections.Generic;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 渲染计划（后端投影的输入）：中立节点树 + 可复算的字段计数。
/// <para>计数 = 结构事实（门禁/Headless 断言用），禁由叙述给出。</para>
/// </summary>
/// <param name="ViewKey">视图键（声明面）。</param>
/// <param name="Roots">根节点（有序）。</param>
public sealed record RenderPlan(string ViewKey, IReadOnlyList<UiSurfaceNode> Roots)
{
    /// <summary>空计划。</summary>
    public static readonly RenderPlan Empty = new(string.Empty, Array.Empty<UiSurfaceNode>());

    /// <summary>语义节点总数（递归）。</summary>
    public int NodeCount => CountNodes(Roots);

    /// <summary>字段条目总数（递归）。</summary>
    public int FieldCount => CountFields(Roots);

    /// <summary>未知占位字段数（应为 0 的覆盖缺口可复算）。</summary>
    public int UnknownFieldCount => CountUnknown(Roots);

    private static int CountNodes(IReadOnlyList<UiSurfaceNode> nodes)
    {
        var count = 0;
        foreach (var node in nodes)
        {
            count += 1 + CountNodes(node.Children);
        }

        return count;
    }

    private static int CountFields(IReadOnlyList<UiSurfaceNode> nodes)
    {
        var count = 0;
        foreach (var node in nodes)
        {
            count += node.Fields.Count + CountFields(node.Children);
        }

        return count;
    }

    private static int CountUnknown(IReadOnlyList<UiSurfaceNode> nodes)
    {
        var count = 0;
        foreach (var node in nodes)
        {
            foreach (var field in node.Fields)
            {
                if (field.IsUnknown) count++;
            }

            count += CountUnknown(node.Children);
        }

        return count;
    }
}
