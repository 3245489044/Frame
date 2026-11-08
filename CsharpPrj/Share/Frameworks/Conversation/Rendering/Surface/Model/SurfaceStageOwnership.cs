#nullable enable

using System;
using System.Collections.Generic;
using UiBackend;

namespace Share.Conversation.Rendering.Surface;

/// <summary>绘制阶段的执行者（**阶段所有权**：Engine 只能接管它拥有的阶段）。</summary>
public enum SurfaceStageOwner
{
    /// <summary>无执行者（阶段未声明）。</summary>
    None = 0,

    /// <summary>载体自绘（GDI+）。</summary>
    CarrierGdi = 1,

    /// <summary>引擎离屏帧（Base/Dynamic 合成后由载体贴图）。</summary>
    EngineFrame = 2,

    /// <summary>引擎场景（3D 玻璃）。</summary>
    EngineScene = 3,
}

/// <summary>
/// 阶段所有权表（唯一入口 <see cref="Resolve"/>）：阶段 → 执行者。
/// <para>背景：旧实现「引擎帧成功后整帧 return」⇒ 页场/chrome/边永不执行；所有权表让 Engine 只接管 Base/Dynamic。</para>
/// </summary>
/// <param name="Stages">阶段 → 执行者（缺项 = <see cref="SurfaceStageOwner.None"/>）。</param>
public sealed record SurfaceStageOwnership(
    IReadOnlyDictionary<SurfacePaintStage, SurfaceStageOwner> Stages)
{
    /// <summary>全部阶段由载体执行（无引擎/未达层）。</summary>
    public static readonly SurfaceStageOwnership Carrier = new(
        new Dictionary<SurfacePaintStage, SurfaceStageOwner>
        {
            [SurfacePaintStage.PageField] = SurfaceStageOwner.CarrierGdi,
            [SurfacePaintStage.Base] = SurfaceStageOwner.CarrierGdi,
            [SurfacePaintStage.Dynamic] = SurfaceStageOwner.CarrierGdi,
            [SurfacePaintStage.Chrome] = SurfaceStageOwner.CarrierGdi,
            [SurfacePaintStage.FrameEdge] = SurfaceStageOwner.CarrierGdi,
        });

    /// <summary>
    /// 引擎帧可用时的所有权：Base/Dynamic 归引擎帧，页场/chrome/边仍归载体。
    /// <para>判据是结构事实「引擎是否产出本帧」，不是能力推测。</para>
    /// </summary>
    public static readonly SurfaceStageOwnership EngineFrameOnly = new(
        new Dictionary<SurfacePaintStage, SurfaceStageOwner>
        {
            // 页场由引擎帧承载（SurfaceAmbient.PaintHostSurface 实际走 TryFrameRaw）⇒ 标称与实现一致（E-14）。
            [SurfacePaintStage.PageField] = SurfaceStageOwner.EngineFrame,
            [SurfacePaintStage.Base] = SurfaceStageOwner.EngineFrame,
            [SurfacePaintStage.Dynamic] = SurfaceStageOwner.EngineFrame,
            [SurfacePaintStage.Chrome] = SurfaceStageOwner.CarrierGdi,
            [SurfacePaintStage.FrameEdge] = SurfaceStageOwner.CarrierGdi,
        });

    /// <summary>该阶段的执行者。</summary>
    /// <param name="stage">绘制阶段。</param>
    public SurfaceStageOwner Of(SurfacePaintStage stage)
        => Stages.TryGetValue(stage, out var owner) ? owner : SurfaceStageOwner.None;

    /// <summary>该阶段是否由指定执行者负责。</summary>
    /// <param name="stage">绘制阶段。</param>
    /// <param name="owner">执行者。</param>
    public bool OwnedBy(SurfacePaintStage stage, SurfaceStageOwner owner) => Of(stage) == owner;

    /// <summary>
    /// 解析所有权：只有引擎合成通道且玻璃层可达时，Base/Dynamic/PageField 归引擎；chrome/边恒归载体。
    /// </summary>
    /// <param name="tier">生效玻璃层（<see cref="GlassTier.Unsupported"/> ⇒ 全部载体）。</param>
    /// <param name="mode">宿主合成通道。</param>
    public static SurfaceStageOwnership Resolve(
        GlassTier tier,
        SurfaceCompositionMode mode)
    {
        if (tier < GlassTier.T0 || mode != SurfaceCompositionMode.EngineComposition)
        {
            return Carrier;
        }

        return EngineFrameOnly;
    }
}
