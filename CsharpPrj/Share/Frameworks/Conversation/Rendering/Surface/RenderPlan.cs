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

/// <summary>一条未知字段的三元组（kind / key / reason）：未知必须可定位、可复算。</summary>
/// <param name="Kind">帧类别。</param>
/// <param name="Key">字段名（来源键）。</param>
/// <param name="Reason">原因码（<see cref="SurfaceReasonCodes"/>）。</param>
public readonly record struct UnknownFieldRecord(string Kind, string Key, string Reason);

/// <summary>合成诊断（P2 DTO）：未知字段清单 + 可见失败清单（单一出口）。</summary>
/// <param name="UnknownFields">未知字段三元组（有序）。</param>
/// <param name="Failures">合成期可见失败码。</param>
public sealed record RenderDiagnostics(
    IReadOnlyList<UnknownFieldRecord> UnknownFields, IReadOnlyList<string> Failures)
{
    /// <summary>空诊断。</summary>
    public static readonly RenderDiagnostics Empty =
        new(Array.Empty<UnknownFieldRecord>(), Array.Empty<string>());
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

    /// <summary>合成诊断（未知字段三元组 + 失败清单；旧构造点为空诊断）。</summary>
    public RenderDiagnostics Diagnostics { get; init; } = RenderDiagnostics.Empty;
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
