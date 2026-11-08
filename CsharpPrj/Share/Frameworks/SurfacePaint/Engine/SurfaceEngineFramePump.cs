#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using System.Threading;
using MathematicalAlgorithms.Surface;
using Share.Conversation.Rendering.Surface;
using Share.SurfacePaint.Hosting;
using UiBackend;

namespace Share.SurfacePaint.Engine;

/// <summary>
/// 引擎帧泵（**载体无关**）：材质声明 → 绘制项 → Engine 帧 → 位图。
/// <para>端口 <see cref="ISurfaceEnginePort"/> 由载体注入（避免框架依赖 Share.Registry/TConfig）。</para>
/// <para>失败返回 null + 原因码（禁静默空位图）。</para>
/// </summary>
public static class SurfaceEngineFramePump
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Bitmap> Cache = new(StringComparer.Ordinal);

    /// <summary>缓存 LRU 序（尾=最近使用；容量上限内只淘汰最旧一项，不再整表清空）。</summary>
    private static readonly LinkedList<string> CacheOrder = new();

    /// <summary>缓存容量上限。</summary>
    private const int CacheCapacity = 24;

    /// <summary>已退役但可能仍被上一帧绘制引用的位图（延迟一代释放，避免绘制中 Dispose）。</summary>
    private static readonly List<Bitmap> Retired = new();

    private static int _renderRequests;
    private static int _cacheHits;

    /// <summary>实际向引擎端口发起的渲染次数（诊断/断言用）。</summary>
    public static int RenderRequests => Volatile.Read(ref _renderRequests);

    /// <summary>帧缓存命中次数（诊断/断言用）。</summary>
    public static int CacheHits => Volatile.Read(ref _cacheHits);

    /// <summary>开关（配置态；off 仅用于对照/排障）。</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>质量档（full/balanced/minimal）。</summary>
    public static string Quality { get; set; } = "balanced";

    /// <summary>最近一次失败原因（诊断）。</summary>
    public static string LastReason { get; private set; } = "not-requested";

    private static long _retryNotBeforeMs;
    private static int _consecutiveFailures;

    /// <summary>连续失败次数（诊断）。</summary>
    public static int ConsecutiveFailures => Volatile.Read(ref _consecutiveFailures);

    /// <summary>下一次允许尝试的时刻（<see cref="Environment.TickCount64"/>；诊断）。</summary>
    public static long RetryNotBeforeMs => Volatile.Read(ref _retryNotBeforeMs);

    private static void RecordFailure(string reason)
    {
        LastReason = reason;
        var failures = Interlocked.Increment(ref _consecutiveFailures);
        var exponent = Math.Min(failures - 1, 5);
        var delay = Math.Min(30_000, 500 * (1 << exponent));
        Interlocked.Exchange(ref _retryNotBeforeMs, Environment.TickCount64 + delay);
    }

    private static void RecordSuccess()
    {
        LastReason = string.Empty;
        Interlocked.Exchange(ref _consecutiveFailures, 0);
        Interlocked.Exchange(ref _retryNotBeforeMs, 0);
    }

    /// <summary>以材质取一帧（静态/动态全含）。</summary>
    public static Bitmap? TryFrame(
        SurfaceViewport viewport, ResolvedMaterial? material, SurfaceLightState? light, string quality,
        ISurfaceEnginePort port, SurfaceTextureFrame? backdrop = null, SurfaceGlassSpec? glass = null,
        ISurfaceEventLog? log = null)
    {
        if (material is null || !viewport.IsValid) return null;
        var scale = viewport.Scale;
        return TryFrameRaw(
            viewport, BuildItems(material, viewport.Bounds, scale, light, backdrop, glass),
            "material:" + material.MaterialId, light, quality, port, backdrop, log);
    }

    /// <summary>
    /// 以显式绘制项取一帧（页面场/chrome 等非材质路径；同一 token 命中缓存）。
    /// <para>所有权：缓存是返回位图的**唯一所有者**，调用方只借用（禁 Dispose，禁跨帧持有）。</para>
    /// </summary>
    public static Bitmap? TryFrameRaw(
        SurfaceViewport viewport, IReadOnlyList<SurfaceDrawItem> items, string contentToken,
        SurfaceLightState? light, string quality, ISurfaceEnginePort port,
        SurfaceTextureFrame? backdrop = null, ISurfaceEventLog? log = null)
    {
        if (!viewport.IsValid) return null;
        if (port is null)
        {
            LastReason = "port-missing";
            return null;
        }

        // 负缓存/退避：设备丢失或连续失败期间不允许每个控件每帧重试。
        if (Environment.TickCount64 < Volatile.Read(ref _retryNotBeforeMs))
        {
            return null;
        }

        // 无光不进 D3D 的判定由调用方（场景/会话）负责；本泵保留零光帧能力（ToLight 的既有语义）。
        var dpi = viewport.Dpi > 0 ? viewport.Dpi : 96;
        SurfaceViewportRegistry.Publish(viewport.Width, viewport.Height, dpi, quality);

        // L-5：帧光签名必须与进程内唯一光帧一致；不一致 ⇒ 显式记账（以 registry 为准，不静默）。
        var registryFrame = SurfaceLightRegistry.Current;
        if (light is SurfaceLightState lightState && lightState.HasValue && registryFrame is not null
            && lightState.LightSignature != registryFrame.Signature)
        {
            LastReason = "light-frame-mismatch";
            log?.Write("surface.light.mismatch", new
            {
                request = lightState.LightSignature,
                registry = registryFrame.Signature,
                content = contentToken,
            });
        }

        var request = new EngineSurfaceRequest(
            viewport.Width, viewport.Height, dpi,
            (light?.CacheToken ?? "no-light") + "|" + contentToken,
            quality, ToLight(light), items, backdrop);

        var token = EngineSurfaceBridge.FrameTokenOf(request, items);
        lock (Gate)
        {
            if (Cache.TryGetValue(token, out var cached))
            {
                Interlocked.Increment(ref _cacheHits);
                Touch(token);
                return cached;
            }
        }

        Interlocked.Increment(ref _renderRequests);
        if (!port.TryRender(request, out var frame, out var reason) || frame is null || !frame.HasPixels)
        {
            RecordFailure(frame is null ? reason : (string.IsNullOrEmpty(frame.Reason) ? "no-pixels" : frame.Reason));
            log?.Write("surface.frame.degraded", new { reason = LastReason, content = contentToken });
            return null;
        }

        var bitmap = ToBitmap(frame, out var conversionReason);
        if (bitmap is null)
        {
            // E-11：像素不完整不得画半帧；失败可见化后回退载体绘制。
            RecordFailure(conversionReason);
            log?.Write("surface.frame.degraded", new { reason = conversionReason, content = contentToken });
            return null;
        }
        lock (Gate)
        {
            // 同一 token 可能已被并发帧写入：以首个命中为准，后者释放自己的副本（缓存所有权唯一）。
            if (Cache.TryGetValue(token, out var existing))
            {
                bitmap.Dispose();
                Interlocked.Increment(ref _cacheHits);
                Touch(token);
                return existing;
            }

            // LRU：只淘汰最旧一项（上一代延迟释放，避免绘制中 Dispose）。
            if (Cache.Count >= CacheCapacity && CacheOrder.First is { } oldest)
            {
                CacheOrder.RemoveFirst();
                if (Cache.Remove(oldest.Value, out var evicted))
                {
                    Retired.Add(evicted);
                }

                while (Retired.Count > CacheCapacity)
                {
                    Retired[0].Dispose();
                    Retired.RemoveAt(0);
                }
            }

            // 查/存键必须同源（P0-1）：缓存键 = 请求指纹 token，不再使用引擎自报 FrameToken。
            Cache[token] = bitmap;
            CacheOrder.AddLast(token);
        }

        RecordSuccess();
        return bitmap;
    }


    /// <summary>把命中项移到 LRU 尾部（调用方持 <see cref="Gate"/>）。</summary>
    private static void Touch(string token)
    {
        var node = CacheOrder.Find(token);
        if (node is null)
        {
            CacheOrder.AddLast(token);
            return;
        }

        if (!ReferenceEquals(node, CacheOrder.Last))
        {
            CacheOrder.Remove(node);
            CacheOrder.AddLast(node);
        }
    }

    /// <summary>清空帧缓存（换肤/测试）。延迟一代释放：本代转入退役区，下次清空时才 Dispose。</summary>
    public static void ClearCache()
    {
        lock (Gate)
        {
            foreach (var stale in Retired) stale.Dispose();
            Retired.Clear();
            foreach (var value in Cache.Values) Retired.Add(value);
            Cache.Clear();
            CacheOrder.Clear();
        }
    }

    private static SurfaceFrameLightData ToLight(SurfaceLightState? light)
    {
        // 无光 ⇒ 零光帧（强度/环境/着色全 0）：引擎照常出像素，但不伪造太阳。
        if (light is not { HasValue: true } value) return default;
        return new SurfaceFrameLightData(
            value.DirectionX, value.DirectionY, value.DirectionZ,
            value.Intensity, 0.18f, value.Warmth, value.TintArgb,
            value.SpotLocalX, value.SpotLocalY, value.SpotRadiusDips, value.Shade);
    }

    private static IReadOnlyList<SurfaceDrawItem> BuildItems(
        ResolvedMaterial material, Rectangle bounds, float scale, SurfaceLightState? light,
        SurfaceTextureFrame? backdrop = null, SurfaceGlassSpec? glass = null)
    {
        var specs = ResolvedLayerSpecProjection.ParseCached(material.Layers);
        var ordered = SurfacePaintPlan.Order(specs.Specs, includeStatic: true, includeDynamic: true);
        var items = new List<SurfaceDrawItem>(ordered.Count + 1);
        var hasBackdropItem = false;

        // 真玻璃基底：只有真实背景源 + 可达玻璃层时才生成（无源不生成，禁以 Fill 冒充）。
        if (backdrop is { Available: true } && glass is { Tier: >= GlassTier.T0 })
        {
            var radius = Math.Max(0f, specs.MaterialRadius) * scale;
            items.Add(new SurfaceDrawItem(
                bounds.X, bounds.Y, bounds.Width, bounds.Height,
                radius, unchecked((uint)glass.TintArgb), 1f, 0f,
                SurfaceDrawKind.Backdrop, SurfaceDrawFlags.Decor,
                glass.BlurSigma, glass.RefractStrength, glass.Dispersion, glass.FresnelStrength,
                textureId: 1));
            hasBackdropItem = true;
        }

        foreach (var spec in ordered)
        {
            // 材质自身的 backdrop 层由上面唯一一条基底项替代（避免双画/双采样）。
            if (hasBackdropItem && spec.Kind == LayerPrimitive.Backdrop) continue;
            var kind = spec.Kind switch
            {
                LayerPrimitive.Fill => SurfaceDrawKind.Fill,
                LayerPrimitive.Gradient => SurfaceDrawKind.Gradient,
                LayerPrimitive.Stroke => SurfaceDrawKind.Edge,
                LayerPrimitive.Shadow => SurfaceDrawKind.Shadow,
                LayerPrimitive.InnerShadow => SurfaceDrawKind.Edge,
                LayerPrimitive.Backdrop => SurfaceDrawKind.Backdrop,
                LayerPrimitive.Specular => SurfaceDrawKind.Spot,
                LayerPrimitive.Texture => SurfaceDrawKind.Texture,
                LayerPrimitive.Decal => SurfaceDrawKind.Texture,
                LayerPrimitive.Noise => SurfaceDrawKind.Noise,
                _ => SurfaceDrawKind.Fill,
            };

            // 无光 ⇒ 不生成 specular 项（不得用零强度伪造高光；高光必须来自 Engine 光帧）。
            if (kind == SurfaceDrawKind.Spot && light is not { HasValue: true })
            {
                continue;
            }

            if (!spec.HasColor && spec.Kind is not (LayerPrimitive.Backdrop or LayerPrimitive.Specular))
            {
                continue;
            }

            var radius = Math.Max(spec.RadiusDeclared, specs.MaterialRadius) * scale;
            var width = spec.Width * scale;
            var blur = spec.Blur * scale;
            var opacity = spec.Kind == LayerPrimitive.Specular ? 0.85f : 1f;
            items.Add(new SurfaceDrawItem(
                bounds.X, bounds.Y, bounds.Width, bounds.Height,
                radius, unchecked((uint)spec.ColorArgb), opacity, spec.Z,
                kind, SurfaceDrawFlags.Decor,
                spec.Angle, width, blur, spec.Strength));
        }

        return items;
    }

    private static Bitmap? ToBitmap(EngineSurfaceFrame frame, out string reason)
    {
        reason = string.Empty;
        var expected = frame.Width * frame.Height * SurfacePixelFormat.BytesPerPixel;
        if (frame.Width <= 0 || frame.Height <= 0 || frame.Rgba.Length < expected)
        {
            // E-11：长度不足即失败（禁静默画半帧）。
            reason = "pixels-truncated";
            return null;
        }

        // 引擎帧 = 直通 RGBA；GDI+ 位图 = 预乘 BGRA。转换只在 SurfacePixelFormat 一处发生。
        var bitmap = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppPArgb);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            var row = new byte[frame.Width * SurfacePixelFormat.BytesPerPixel];
            var source = frame.Rgba;
            for (var y = 0; y < frame.Height; y++)
            {
                var offset = y * frame.Width * SurfacePixelFormat.BytesPerPixel;
                if (offset + row.Length > source.Length)
                {
                    reason = "pixels-truncated";
                    bitmap.Dispose();
                    return null;
                }
                SurfacePixelFormat.RgbaToPremultipliedBgra(
                    new ReadOnlySpan<byte>(source, offset, row.Length), row);
                Marshal.Copy(row, 0, data.Scan0 + y * data.Stride, row.Length);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }
}

