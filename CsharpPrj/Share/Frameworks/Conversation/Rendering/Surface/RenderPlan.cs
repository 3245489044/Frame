#nullable enable

using System;
using System.Collections.Generic;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 帧计划计数（**单源** = <see cref="FrameComposer"/> 合成时由 <c>AttachPlanner.Plan</c>/<c>PlanGroups</c> 产出）：
/// <see cref="Total"/> = 输入帧数，<see cref="Rows"/> = 实际呈现行数（段首行 + 锚点行 + 组内行），
/// <see cref="Merged"/> = 并入调用行的结果帧数。
/// <para>恒等式 <c>Total == Rows + Merged</c>（附加未呈现分量后为 <c>Total == Rows + Merged + NotPresented</c>）。</para>
/// </summary>
public sealed record RenderPlanCounts(int Total, int Rows, int Merged)
{
    /// <summary>空计数。</summary>
    public static readonly RenderPlanCounts Empty = new(0, 0, 0);

    /// <summary>恒等式成立（行 + 合并 = 输入）。</summary>
    public bool IsBalanced => Total == Rows + Merged;
}

/// <summary>
/// 渲染计划（后端投影的输入）：中立节点树 + 可复算的字段计数。
/// <para>计数 = 结构事实（门禁/Headless 断言用），禁由叙述给出。</para>
/// </summary>
/// <param name="ViewKey">视图键（声明面）。</param>
/// <param name="Roots">根节点（有序）。</param>
/// <param name="Counts">帧计划计数（合成时产出；旧构造点可为 null，消费方按未知处理）。</param>
public sealed record RenderPlan(string ViewKey, IReadOnlyList<UiSurfaceNode> Roots)
{
    /// <summary>帧计划计数（合成时产出；旧构造点可为 null，消费方按未知处理）。</summary>
    public RenderPlanCounts? Counts { get; init; }
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
