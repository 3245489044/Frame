#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
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

    /// <summary>开关（配置态；off 仅用于对照/排障）。</summary>
    public static bool Enabled { get; set; } = true;

    /// <summary>质量档（full/balanced/minimal）。</summary>
    public static string Quality { get; set; } = "balanced";

    /// <summary>最近一次失败原因（诊断）。</summary>
    public static string LastReason { get; private set; } = "not-requested";

    /// <summary>以材质取一帧（静态/动态全含）。</summary>
    public static Bitmap? TryFrame(
        SurfaceViewport viewport, ResolvedMaterial? material, SurfaceLightState? light, string quality,
        ISurfaceEnginePort port, ISurfaceEventLog? log = null)
    {
        if (material is null || !viewport.IsValid) return null;
        var scale = viewport.Scale;
        return TryFrameRaw(
            viewport, BuildItems(material, viewport.Bounds, scale, light), "material:" + material.MaterialId,
            light, quality, port, log);
    }

    /// <summary>以显式绘制项取一帧（页面场/chrome 等非材质路径；同一 token 命中缓存）。</summary>
    public static Bitmap? TryFrameRaw(
        SurfaceViewport viewport, IReadOnlyList<SurfaceDrawItem> items, string contentToken,
        SurfaceLightState? light, string quality, ISurfaceEnginePort port, ISurfaceEventLog? log = null)
    {
        if (!viewport.IsValid) return null;
        if (port is null)
        {
            LastReason = "port-missing";
            return null;
        }

        var dpi = viewport.Dpi > 0 ? viewport.Dpi : 96;
        SurfaceViewportRegistry.Publish(viewport.Width, viewport.Height, dpi, quality);
        var request = new EngineSurfaceRequest(
            viewport.Width, viewport.Height, dpi,
            (light?.CacheToken ?? "no-light") + "|" + contentToken,
            quality, ToLight(light), items);

        var token = EngineSurfaceBridge.FrameTokenOf(request, items);
        lock (Gate)
        {
            if (Cache.TryGetValue(token, out var cached)) return cached;
        }

        if (!port.TryRender(request, out var frame, out var reason) || frame is null || !frame.HasPixels)
        {
            LastReason = frame is null ? reason : (string.IsNullOrEmpty(frame.Reason) ? "no-pixels" : frame.Reason);
            log?.Write("surface.frame.degraded", new { reason = LastReason, content = contentToken });
            return null;
        }

        var bitmap = ToBitmap(frame);
        lock (Gate)
        {
            if (Cache.Count > 24)
            {
                foreach (var value in Cache.Values) value.Dispose();
                Cache.Clear();
            }

            Cache[frame.FrameToken] = bitmap;
        }

        LastReason = string.Empty;
        return bitmap;
    }

    /// <summary>清空帧缓存（换肤/测试）。</summary>
    public static void ClearCache()
    {
        lock (Gate)
        {
            foreach (var value in Cache.Values) value.Dispose();
            Cache.Clear();
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
        ResolvedMaterial material, Rectangle bounds, float scale, SurfaceLightState? light)
    {
        var specs = ResolvedLayerSpecProjection.ParseCached(material.Layers);
        var ordered = SurfacePaintPlan.Order(specs.Specs, includeStatic: true, includeDynamic: true);
        var items = new List<SurfaceDrawItem>(ordered.Count);
        foreach (var spec in ordered)
        {
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

    private static Bitmap ToBitmap(EngineSurfaceFrame frame)
    {
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
                if (offset + row.Length > source.Length) break;
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

