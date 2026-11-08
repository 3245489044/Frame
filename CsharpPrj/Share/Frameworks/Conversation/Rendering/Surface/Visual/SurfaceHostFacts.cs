#nullable enable

using System;
using UiBackend;

namespace Share.Conversation.Rendering.Surface;

/// <summary>宿主合成事实快照（唯一读取面；由宿主装配期登记）。</summary>
/// <param name="Composition">合成能力。</param>
/// <param name="Backdrop">背后内容源。</param>
/// <param name="Budget">帧预算。</param>
/// <param name="DeclaredTier">材质声明上限（null = 未声明）。</param>
public sealed record SurfaceHostFactsSnapshot(
    SurfaceCompositionCapability Composition,
    SurfaceTextureFrame Backdrop,
    SurfaceFrameBudget Budget,
    GlassTier? DeclaredTier);

/// <summary>
/// 宿主合成事实（**唯一存储**）：装配入口写入，载体/Gather 只读。
/// <para>跨层共享点：Frame 侧载体与 Projects 侧声明点读同一份事实。</para>
/// </summary>
public static class SurfaceHostFacts
{
    private static readonly object Gate = new();
    private static SurfaceCompositionCapability _composition = SurfaceCompositionCapability.Unavailable;
    private static SurfaceTextureFrame _backdrop = SurfaceTextureFrame.Unavailable;
    private static SurfaceFrameBudget _budget = SurfaceFrameBudget.Default;
    private static GlassTier? _declaredTier;

    /// <summary>当前事实快照。</summary>
    public static SurfaceHostFactsSnapshot Current
    {
        get
        {
            lock (Gate)
            {
                return new SurfaceHostFactsSnapshot(_composition, _backdrop, _budget, _declaredTier);
            }
        }
    }

    /// <summary>登记合成能力（装配期一次）。</summary>
    public static void DeclareComposition(SurfaceCompositionCapability? composition)
    {
        lock (Gate)
        {
            _composition = composition ?? SurfaceCompositionCapability.Unavailable;
        }
    }

    /// <summary>登记背景源（可为空 ⇒ 显式不可用）。</summary>
    public static void DeclareBackdrop(SurfaceTextureFrame? backdrop)
    {
        lock (Gate)
        {
            _backdrop = backdrop ?? SurfaceTextureFrame.Unavailable;
        }
    }

    /// <summary>登记背景源（旧类型过渡重载）。</summary>
    public static void DeclareBackdrop(SurfaceBackdropFrame? backdrop)
        => DeclareBackdrop(backdrop is null ? null : backdrop.ToTextureFrame());

    /// <summary>登记帧预算。</summary>
    public static void DeclareBudget(SurfaceFrameBudget? budget)
    {
        lock (Gate)
        {
            _budget = budget ?? SurfaceFrameBudget.Default;
        }
    }

    /// <summary>登记材质声明上限（null = 未声明）。</summary>
    public static void DeclareTier(GlassTier? tier)
    {
        lock (Gate)
        {
            _declaredTier = tier;
        }
    }

    /// <summary>当前玻璃选层（唯一计算入口）。</summary>
    public static SurfaceGlassSpec Glass()
    {
        var facts = Current;
        return SurfaceGlassSpec.Clamp(
            facts.Composition, facts.Backdrop.FromTextureFrame(), facts.DeclaredTier, facts.Budget);
    }

    /// <summary>清空事实（测试/重装配；E-21）。</summary>
    public static void Reset()
    {
        lock (Gate)
        {
            _composition = SurfaceCompositionCapability.Unavailable;
            _backdrop = SurfaceTextureFrame.Unavailable;
            _budget = SurfaceFrameBudget.Default;
            _declaredTier = null;
        }
    }
}
