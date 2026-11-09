#nullable enable

using System;
using System.Drawing;
using Share.Conversation.Rendering.Surface;
using SurfacePaint.Contracts;
using UiBackend;

namespace Share.SurfacePaint.Hosting;

/// <summary>渲染后端类别（同一帧只允许一个）。</summary>
public enum SurfaceBackendKind
{
    /// <summary>GDI+ 离屏合成（始终可用）。</summary>
    Cpu,

    /// <summary>引擎帧泵（就绪才可调用）。</summary>
    Engine,
}

/// <summary>
/// 一张卡片的**完整面**（Base + Dynamic + Chrome + FrameEdge 已在位图内合成）。
/// <para><see cref="Inherited"/> = 继承父面（不产面）；<see cref="Face"/> 为 null 且非继承 ⇒ 失败。</para>
/// <para>所有权：<see cref="Owned"/> = true（CPU 自有，由 Store 释放）；false = 借用（引擎帧泵）。</para>
/// </summary>
public sealed record SurfaceCardFace(
    string Key,
    Rectangle Bounds,
    string MaterialId,
    string MaterialKey,
    string Role,
    int Depth,
    Bitmap? Face,
    bool Owned,
    bool Inherited,
    string Fingerprint)
{
    /// <summary>继承条目（嵌套/叶子不产面）。</summary>
    /// <param name="card">卡片几何。</param>
    /// <param name="entry">装饰条目。</param>
    public static SurfaceCardFace Inherit(SurfaceCardGeometry card, SurfaceDecorEntry entry)
        => new(card.Key, card.BoundsDip, entry.MaterialSlot, entry.MaterialSlot, entry.Role, entry.Depth,
            null, false, true, "inherit:" + card.Key + ":" + entry.Tier);

    /// <summary>释放自有位图（借用帧不得释放）。</summary>
    public void Release()
    {
        if (Owned) Face?.Dispose();
    }
}

/// <summary>
/// 单卡合成请求（唯一输入形态；Composer 构造，后端只读）。
/// </summary>
/// <param name="Card">卡片几何。</param>
/// <param name="Decor">装饰条目。</param>
/// <param name="Plan">计划产物（材质唯一来源）。</param>
/// <param name="Light">窗口级光态。</param>
/// <param name="Facts">本帧渲染事实。</param>
/// <param name="Viewport">窗口视口。</param>
/// <param name="Budget">帧预算。</param>
/// <param name="Backdrop">窗口级背后纹理（可空）。</param>
public sealed record SurfaceBackendRequest(
    SurfaceCardGeometry Card,
    SurfaceDecorEntry Decor,
    SurfacePlanBundle Plan,
    SurfaceLightState? Light,
    SurfaceRenderFacts? Facts,
    SurfaceViewport Viewport,
    SurfaceFrameBudget Budget,
    SurfaceTextureFrame? Backdrop = null);

/// <summary>
/// 渲染后端（CPU / Engine 二选一；同一帧不得双跑）。
/// </summary>
public interface ISurfaceRenderBackend
{
    /// <summary>后端类别。</summary>
    SurfaceBackendKind Kind { get; }

    /// <summary>就绪态（未 Ready 不得调用）。</summary>
    PortStatus Status { get; }

    /// <summary>产整窗页面场（失败 ⇒ null，Composer 判 Blocked）。</summary>
    Bitmap? RenderPageField(
        SurfaceDecorBundle decor,
        SurfaceViewport viewport,
        SurfaceLightState? light,
        SurfaceRenderFacts facts,
        SurfaceFrameBudget budget);

    /// <summary>产单卡完整面（失败 ⇒ false + reason）。</summary>
    bool TryRender(SurfaceBackendRequest request, out Bitmap? face, out string reason);
}

/// <summary>
/// 一轮聚拢溢出的帧级事实（Composer 的唯一输入集合）。
/// </summary>
/// <param name="Plan">计划产物。</param>
/// <param name="Decor">装饰产物。</param>
/// <param name="Light">窗口级光态。</param>
/// <param name="Pointer">指针输入。</param>
/// <param name="Budget">帧预算。</param>
/// <param name="Facts">渲染事实。</param>
/// <param name="Composition">合成能力。</param>
/// <param name="Viewport">窗口视口。</param>
public sealed record SurfaceGatherFrame(
    SurfacePlanBundle Plan,
    SurfaceDecorBundle Decor,
    SurfaceLightState? Light,
    SurfacePointerState Pointer,
    SurfaceFrameBudget Budget,
    SurfaceRenderFacts? Facts,
    SurfaceCompositionCapability Composition,
    SurfaceViewport Viewport)
{
    /// <summary>空帧事实（未装配；显式降级）。</summary>
    public static SurfaceGatherFrame Empty(SurfaceViewport viewport) => new(
        SurfacePlanBundle.Empty,
        SurfaceDecorBundle.Empty,
        null,
        SurfacePointerState.Unavailable,
        SurfaceFrameBudget.Default,
        null,
        SurfaceCompositionCapability.Unavailable,
        viewport);
}
