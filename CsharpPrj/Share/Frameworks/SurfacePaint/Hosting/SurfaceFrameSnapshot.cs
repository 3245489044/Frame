#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using Share.Conversation.Rendering.Surface;
using SurfacePaint.Contracts;
using UiBackend;

namespace Share.SurfacePaint.Hosting;

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

    /// <summary>装饰层是否启用（帧证据：ambient=on/off）。</summary>
    public bool AmbientEnabled { get; init; }

    /// <summary>质量档（帧证据：minimal/balanced/full）。</summary>
    public string QualityProfile { get; init; } = string.Empty;

    /// <summary>引擎后端是否就绪（帧证据：backend=cpu|engine 的判定事实）。</summary>
    public bool EngineReady { get; init; }

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

    /// <summary>
    /// 释放自有位图（引擎借用帧不在此释放；由帧泵 LRU 管理）。
    /// </summary>
    public void ReleaseFrames()
    {
        PageField?.Dispose();
        foreach (var card in Cards.Values) card.Release();
    }
}

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

