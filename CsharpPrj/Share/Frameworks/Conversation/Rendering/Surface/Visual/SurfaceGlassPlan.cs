#nullable enable

using System;
using System.Collections.Generic;
using UiBackend;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 玻璃计划（**装配与降级账本**）：选层结论 + 阶段所有权 + 降级原因。
/// <para>选层仍由 <see cref="SurfaceGlassSpec.Clamp"/> 单一裁决；本类不复制选层算法。</para>
/// </summary>
/// <param name="Spec">选层结论（tier/参数/fallback/reason）。</param>
/// <param name="Ownership">阶段所有权（Engine 只接管它拥有的阶段）。</param>
/// <param name="Degradations">降级原因码（稳定去重）。</param>
/// <param name="Reason">计划级原因（可用时为空）。</param>
public sealed record SurfaceGlassPlan(
    SurfaceGlassSpec Spec,
    SurfaceStageOwnership Ownership,
    IReadOnlyList<string> Degradations,
    string Reason)
{
    /// <summary>生效层级。</summary>
    public GlassTier Tier => Spec.Tier;

    /// <summary>未达层时的降级形态。</summary>
    public SurfaceGlassFallback Fallback => Spec.Fallback;

    /// <summary>该阶段是否由引擎帧执行。</summary>
    public bool EngineOwns(SurfacePaintStage stage)
        => Ownership.OwnedBy(stage, SurfaceStageOwner.EngineFrame);

    /// <summary>
    /// 装配计划：<see cref="SurfaceGlassSpec.Clamp"/> → <see cref="SurfaceStageOwnership.Resolve"/> → 降级账本。
    /// </summary>
    public static SurfaceGlassPlan Negotiate(
        SurfaceCompositionCapability? composition,
        SurfaceTextureFrame? backdrop,
        SurfaceFrameBudget? budget,
        GlassTier? declared)
        => Negotiate(composition, backdrop, budget, declared, SurfaceGlassLookTable.BuiltIn);

    /// <summary>
    /// 装配计划（声明值版）：玻璃形态从 <paramref name="look"/> 读取。
    /// </summary>
    /// <param name="composition">合成能力。</param>
    /// <param name="backdrop">背后内容源。</param>
    /// <param name="budget">帧预算。</param>
    /// <param name="declared">声明层级上限。</param>
    /// <param name="look">玻璃形态声明表。</param>
    public static SurfaceGlassPlan Negotiate(
        SurfaceCompositionCapability? composition,
        SurfaceTextureFrame? backdrop,
        SurfaceFrameBudget? budget,
        GlassTier? declared,
        SurfaceGlassLookTable? look)
    {
        var spec = SurfaceGlassSpec.Clamp(composition, backdrop.FromTextureFrame(), declared, budget, look);
        var ownership = SurfaceStageOwnership.Resolve(spec.Tier, composition?.Mode ?? SurfaceCompositionMode.None);
        var degradations = new List<string>();
        if (!string.IsNullOrWhiteSpace(spec.Reason)) degradations.Add(spec.Reason!);
        if (backdrop is null || !backdrop.Available)
        {
            degradations.Add(string.IsNullOrWhiteSpace(backdrop?.Reason) ? "backdrop-unavailable" : backdrop!.Reason);
        }

        foreach (var stage in Enum.GetValues<SurfacePaintStage>())
        {
            if (ownership.Of(stage) == SurfaceStageOwner.None) degradations.Add("stage-orphan:" + stage);
        }

        return new SurfaceGlassPlan(
            spec,
            ownership,
            SurfaceRenderFacts.Distinct(degradations),
            spec.Reason ?? string.Empty);
    }
}
