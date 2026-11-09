#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using Share.Conversation.Rendering.Surface;
using SurfacePaint.Contracts;
using UiBackend;

namespace Share.SurfacePaint.Hosting;

/// <summary>
/// 一帧内某控件的旧形态（**兼容读取面**；新代码用 <see cref="SurfaceCardFace"/>）。
/// </summary>
[Obsolete("使用 SurfaceCardFace；本类型仅保留一版用于旧的 SurfaceFrameSnapshot.LegacyCards 读取。")]
public sealed record SurfaceCardFrame(
    string Key,
    Rectangle Bounds,
    string MaterialId,
    string MaterialKey,
    Bitmap? Frame,
    bool EngineOwnsFace,
    string Fingerprint);

/// <summary>
/// 窗口级不可变帧快照（<c>IRefreshRenderer</c> 的唯一产物；UI 只读）。
/// <para>承载「一窗口一帧」：页面场 + 各卡片完整面 + 光 + 渲染事实 + 指针 + 降级清单。</para>
/// <para>生命周期：由帧生产者持有；旧代由 Store 释放（自有位图），消费者不得 Dispose。</para>
/// </summary>
public sealed record SurfaceFrameSnapshot(
    string WindowId,
    long Generation,
    SurfaceViewport Viewport,
    SurfaceLightState? Light,
    SurfaceRenderFacts? Facts,
    Bitmap? PageField,
    IReadOnlyDictionary<string, SurfaceCardFace> Cards,
    IReadOnlyList<string> Degradations,
    string Fingerprint,
    long CreatedAtMs)
{
    /// <summary>指针输入（阶段一进快照；<see cref="SurfacePointerState.Unavailable"/> = 无指针源）。</summary>
    public SurfacePointerState Pointer { get; init; }

    /// <summary>结构化诊断（与 <see cref="Degradations"/> 同源；供帧证据读取）。</summary>
    public IReadOnlyList<string> Diagnostics => Degradations;

    /// <summary>空帧（未生产/未就绪；消费者应保持上一帧或走 CPU 路径）。</summary>
    public static readonly SurfaceFrameSnapshot Empty = new(
        string.Empty,
        0,
        SurfaceViewport.Empty,
        null,
        null,
        null,
        new Dictionary<string, SurfaceCardFace>(StringComparer.Ordinal),
        Array.Empty<string>(),
        "empty",
        0);

    /// <summary>是否有可消费内容。</summary>
    public bool HasContent => PageField is not null || Cards.Count > 0;

    /// <summary>阻塞帧（后端失败，不贴旧帧）。</summary>
    /// <param name="windowId">窗口 id。</param>
    /// <param name="reason">原因码。</param>
    public static SurfaceFrameSnapshot Blocked(string windowId, string reason)
        => Empty with
        {
            WindowId = windowId,
            Degradations = new[] { reason },
            Fingerprint = "blocked:" + reason,
        };

    /// <summary>旧形态读取面（一版过渡；Frame 来自 <see cref="SurfaceCardFace.Face"/>）。</summary>
#pragma warning disable CS0618
    public IReadOnlyDictionary<string, SurfaceCardFrame> LegacyCards
    {
        get
        {
            var legacy = new Dictionary<string, SurfaceCardFrame>(StringComparer.Ordinal);
            foreach (var pair in Cards)
            {
                var card = pair.Value;
                legacy[pair.Key] = new SurfaceCardFrame(
                    card.Key, card.Bounds, card.MaterialId, card.MaterialKey,
                    card.Face, EngineOwnsFace: !card.Owned && !card.Inherited, card.Fingerprint);
            }

            return legacy;
        }
    }
#pragma warning restore CS0618

    /// <summary>
    /// 释放自有位图（引擎借用帧不在此释放；由帧泵 LRU 管理）。
    /// </summary>
    public void ReleaseFrames()
    {
        PageField?.Dispose();
        foreach (var card in Cards.Values) card.Release();
    }
}

/// <summary>一张卡片的帧生产请求（旧形态；新代码用 <see cref="SurfaceCardGeometry"/>）。</summary>
/// <param name="Key">控件稳定键。</param>
/// <param name="Viewport">控件视口。</param>
/// <param name="Material">已解析材质。</param>
/// <param name="Backdrop">本帧背后快照（可空）。</param>
public sealed record SurfaceCardRequest(
    string Key,
    SurfaceViewport Viewport,
    ResolvedMaterial Material,
    SurfaceTextureFrame? Backdrop);

/// <summary>一次窗口帧生产的输入（不可变；采集后即可离开 UI 线程）。</summary>
/// <param name="WindowId">窗口稳定 id。</param>
/// <param name="Viewport">窗口视口。</param>
/// <param name="Light">窗口级光态（可空 ⇒ 无光）。</param>
/// <param name="Cards">卡片几何（只含几何/宿主键，不含材质）。</param>
/// <param name="Degradations">采集期降级原因。</param>
/// <param name="Backdrop">窗口级背后纹理（可空）。</param>
public sealed record SurfaceFrameInput(
    string WindowId,
    SurfaceViewport Viewport,
    SurfaceLightState? Light,
    IReadOnlyList<SurfaceCardGeometry> Cards,
    IReadOnlyList<string> Degradations,
    SurfaceTextureFrame? Backdrop = null)
{
    /// <summary>指针输入（窗口客户区 DIP）。</summary>
    public SurfacePointerState Pointer { get; init; }
}

/// <summary>
/// 帧消费者契约（载体实现）：只接快照、只贴图；禁自行请求引擎/解析光/采集背景。
/// </summary>
public interface ISurfaceFrameConsumer
{
    /// <summary>消费者稳定键（与 <see cref="SurfaceCardFace.Key"/> 对齐）。</summary>
    string Key { get; }

    /// <summary>接收新帧（UI 线程；实现方只做贴图/失效，不做重计算）。</summary>
    void ApplySnapshot(SurfaceFrameSnapshot snapshot);
}

