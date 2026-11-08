#nullable enable

using System;
using System.Collections.Generic;
using UiBackend;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 单条绘制指令（阶段 + 已解析层），**不含 Graphics**。
/// </summary>
/// <param name="Stage">载体无关阶段（见 <see cref="SurfacePaintStage"/>）。</param>
/// <param name="Spec">已解析层（几何/颜色/图元）。</param>
public sealed record SurfacePaintStep(SurfacePaintStage Stage, ResolvedLayerSpec Spec);

/// <summary>
/// 绘制计划（**载体无关**）：把已解析层栈排成唯一落笔序列。
/// <para>相位序 = <see cref="LayerPhase"/>；同相位内按 Z。载体只负责把序列落成自己的图元调用，不再自行排序。</para>
/// </summary>
public static class SurfacePaintPlan
{
    /// <summary>层相位 → 装饰阶段（Sheen = 随光层，其余 = 面基底）。</summary>
    /// <param name="phase">层相位。</param>
    public static SurfacePaintStage StageOf(LayerPhase phase)
        => phase == LayerPhase.Sheen ? SurfacePaintStage.Dynamic : SurfacePaintStage.Base;

    /// <summary>按相位/Z 排序，并按静态/动态拆分过滤（过滤先于排序，语义与旧 OrderedSpecs 逐条相同）。</summary>
    /// <param name="specs">已解析层栈。</param>
    /// <param name="includeStatic">是否包含非 Sheen 层。</param>
    /// <param name="includeDynamic">是否包含 Sheen 层。</param>
    public static IReadOnlyList<ResolvedLayerSpec> Order(
        IReadOnlyList<ResolvedLayerSpec> specs, bool includeStatic, bool includeDynamic)
    {
        ArgumentNullException.ThrowIfNull(specs);
        var ordered = new List<ResolvedLayerSpec>(specs.Count);
        foreach (var spec in specs)
        {
            var dynamicLayer = spec.Phase == LayerPhase.Sheen;
            if (dynamicLayer && !includeDynamic) continue;
            if (!dynamicLayer && !includeStatic) continue;
            ordered.Add(spec);
        }

        ordered.Sort(static (left, right) =>
        {
            var phaseOrder = (int)left.Phase - (int)right.Phase;
            return phaseOrder != 0 ? phaseOrder : left.Z - right.Z;
        });
        return ordered;
    }

    /// <summary>有序指令表（阶段标签 + 层）；顺序即落笔顺序。</summary>
    /// <param name="specs">已解析层栈。</param>
    /// <param name="includeStatic">是否包含非 Sheen 层。</param>
    /// <param name="includeDynamic">是否包含 Sheen 层。</param>
    public static IReadOnlyList<SurfacePaintStep> Build(
        IReadOnlyList<ResolvedLayerSpec> specs, bool includeStatic, bool includeDynamic)
    {
        var ordered = Order(specs, includeStatic, includeDynamic);
        var steps = new List<SurfacePaintStep>(ordered.Count);
        foreach (var spec in ordered) steps.Add(new SurfacePaintStep(StageOf(spec.Phase), spec));
        return steps;
    }
}
