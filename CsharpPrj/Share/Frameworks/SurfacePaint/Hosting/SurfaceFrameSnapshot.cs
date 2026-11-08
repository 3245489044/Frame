#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using Share.Conversation.Rendering.Surface;
using Share.SurfacePaint.Materials;
using UiBackend;

namespace Share.SurfacePaint.Hosting;

/// <summary>
/// 一帧内某控件的预渲染结果（不可变；UI 只读）。
/// <para><see cref="Frame"/> 为引擎帧切片；null ⇒ 该控件继续走 CPU/GDI 载体路径。</para>
/// </summary>
/// <param name="Key">控件稳定键（与 <see cref="SurfacePaintSession"/> 宿主 id 同源）。</param>
/// <param name="Bounds">控件客户区矩形（原点 0,0）。</param>
/// <param name="MaterialId">材质声明 id。</param>
/// <param name="MaterialKey">材质指纹（缓存/对账）。</param>
/// <param name="Frame">引擎帧（可空）。</param>
/// <param name="EngineOwnsFace">Base/Dynamic 是否由引擎帧承载。</param>
/// <param name="Fingerprint">本卡片指纹（尺寸/材质/光）。</param>
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
/// <para>承载「一窗口一帧」：页面场 + 各卡片预渲染层 + 光 + 渲染事实 + 降级清单。</para>
/// <para>生命周期：由帧生产者持有；旧代延迟释放，消费者不得 Dispose。</para>
/// </summary>
/// <param name="WindowId">窗口稳定 id。</param>
/// <param name="Generation">代次（单调递增）。</param>
/// <param name="Viewport">窗口视口。</param>
/// <param name="Light">本帧唯一光态（窗口级采样）。</param>
/// <param name="Facts">本帧渲染事实（光/背景/玻璃/所有权/降级）。</param>
/// <param name="PageField">页面场位图（可空；null ⇒ 载体 CPU 降级）。</param>
/// <param name="Cards">控件键 → 卡片帧。</param>
/// <param name="Degradations">降级原因码（稳定去重）。</param>
/// <param name="Fingerprint">整帧结构指纹（不含时间/代次）。</param>
/// <param name="CreatedAtMs">创建时刻（<see cref="Environment.TickCount64"/>）。</param>
public sealed record SurfaceFrameSnapshot(
    string WindowId,
    long Generation,
    SurfaceViewport Viewport,
    SurfaceLightState? Light,
    SurfaceRenderFacts? Facts,
    Bitmap? PageField,
    IReadOnlyDictionary<string, SurfaceCardFrame> Cards,
    IReadOnlyList<string> Degradations,
    string Fingerprint,
    long CreatedAtMs)
{
    /// <summary>空帧（未生产/未就绪；消费者应保持上一帧或走 CPU 路径）。</summary>
    public static readonly SurfaceFrameSnapshot Empty = new(
        string.Empty,
        0,
        SurfaceViewport.Empty,
        null,
        null,
        null,
        new Dictionary<string, SurfaceCardFrame>(StringComparer.Ordinal),
        Array.Empty<string>(),
        "empty",
        0);

    /// <summary>是否有可消费内容。</summary>
    public bool HasContent => PageField is not null || Cards.Count > 0;

    /// <summary>
    /// 释放自有副本（引擎卡片帧为**借用**，所有权归帧泵，禁在此 Dispose）。
    /// <para>消费方只读快照；释放由生产者/存储按代次推进。</para>
    /// </summary>
    public void ReleaseFrames() => PageField?.Dispose();
}

/// <summary>一张卡片的帧生产请求（宿主在 UI 线程采集，组合器在后台消费）。</summary>
/// <param name="Key">控件稳定键。</param>
/// <param name="Viewport">控件视口。</param>
/// <param name="Material">已解析材质。</param>
/// <param name="Backdrop">本帧背后快照（可空；由宿主按需采集）。</param>
public sealed record SurfaceCardRequest(
    string Key,
    SurfaceViewport Viewport,
    ResolvedMaterial Material,
    SurfaceTextureFrame? Backdrop);

/// <summary>一次窗口帧生产的输入（不可变；采集后即可离开 UI 线程）。</summary>
/// <param name="WindowId">窗口稳定 id。</param>
/// <param name="Viewport">窗口视口。</param>
/// <param name="Light">窗口级光态（可空 ⇒ 无光）。</param>
/// <param name="Cards">卡片请求集。</param>
/// <param name="Degradations">采集期降级原因。</param>
public sealed record SurfaceFrameInput(
    string WindowId,
    SurfaceViewport Viewport,
    SurfaceLightState? Light,
    IReadOnlyList<SurfaceCardRequest> Cards,
    IReadOnlyList<string> Degradations,
    SurfaceTextureFrame? Backdrop = null);

/// <summary>
/// 帧消费者契约（载体实现）：只接快照、只贴图；禁自行请求引擎/解析光/采集背景。
/// </summary>
public interface ISurfaceFrameConsumer
{
    /// <summary>消费者稳定键（与 <see cref="SurfaceCardFrame.Key"/> 对齐）。</summary>
    string Key { get; }

    /// <summary>接收新帧（UI 线程；实现方只做贴图/失效，不做重计算）。</summary>
    void ApplySnapshot(SurfaceFrameSnapshot snapshot);
}
