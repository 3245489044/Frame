#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Imaging;
using System.Runtime.InteropServices;
using MathematicalAlgorithms.Surface;
using Share.Conversation.Rendering.Surface;
using Share.Registry;
using Share.SurfacePaint.Engine;
using TConfig;
using UiBackend;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>引擎帧泵：材质声明 → 绘制项 → Engine_SurfaceFrame → 位图（失败返回 null + 原因码）。</summary>
internal static class SurfaceFramePump
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, Bitmap> Cache = new(StringComparer.Ordinal);
    private static string _lastReason = "not-requested";

    /// <summary>开关（配置态 surface_engine=on|off；off 仅用于对照/排障）。</summary>
    internal static bool Enabled { get; set; } = true;

    /// <summary>质量档（full/balanced/minimal）。</summary>
    internal static string Quality { get; set; } = "balanced";

    internal static string LastReason => _lastReason;

    internal static Bitmap? TryFrame(
        Control control, ResolvedMaterial? material, SurfaceLightState? light, string quality)
    {
        if (material is null) return null;
        var bounds = new Rectangle(Point.Empty, control.ClientSize);
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;
        var scale = control.DeviceDpi > 0 ? control.DeviceDpi / 96f : 1f;
        return TryFrameRaw(control, BuildItems(material, bounds, scale), "material:" + material.MaterialId, light, quality);
    }

    /// <summary>以显式绘制项取一帧（页面场/chrome 等非材质路径；同一 token 命中缓存）。</summary>
    internal static Bitmap? TryFrameRaw(
        Control control, IReadOnlyList<MathematicalAlgorithms.Surface.SurfaceDrawItem> items,
        string contentToken, SurfaceLightState? light, string quality)
    {
        var bounds = new Rectangle(Point.Empty, control.ClientSize);
        if (bounds.Width <= 0 || bounds.Height <= 0) return null;
        if (!TryPort(out var port))
        {
            _lastReason = "port-missing";
            return null;
        }

        var dpi = control.DeviceDpi > 0 ? control.DeviceDpi : 96;
        SurfaceViewportRegistry.Publish(bounds.Width, bounds.Height, dpi, quality);
        var request = new EngineSurfaceRequest(
            bounds.Width, bounds.Height, dpi,
            (light?.CacheToken ?? "no-light") + "|" + contentToken,
            quality, ToLight(light), items);

        var token = EngineSurfaceBridge.FrameTokenOf(request, items);
        lock (Gate)
        {
            if (Cache.TryGetValue(token, out var cached)) return cached;
        }

        var frame = EngineSurfaceBridge.TryRender(port, request, out var reason);
        if (frame is null || !frame.HasPixels)
        {
            _lastReason = frame is null ? reason : (string.IsNullOrEmpty(frame.Reason) ? "no-pixels" : frame.Reason);
            SkinDiagnostics.RecordLightDegraded("surface-frame:" + _lastReason, string.Empty);
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

        _lastReason = string.Empty;
        return bitmap;
    }

    private static bool TryPort(out Func<string, string>? port)
    {
        port = null;
        if (RegistryCoordinator.Registry is not ITrConfigRegistry registry) return false;
        if (!registry.TryGetDelegate("Engine_SurfaceFrame", out var function)) return false;
        port = function as Func<string, string>;
        return port is not null;
    }

    private static SurfaceFrameLightData ToLight(SurfaceLightState? light)
    {
        if (light is not { HasValue: true } value) return SurfaceFrameLightData.Default;
        return new SurfaceFrameLightData(
            value.DirectionX, value.DirectionY, value.DirectionZ,
            value.Intensity, 0.18f, value.Warmth, value.TintArgb,
            value.SpotLocalX, value.SpotLocalY, value.SpotRadiusDips, value.Shade);
    }

    private static IReadOnlyList<MathematicalAlgorithms.Surface.SurfaceDrawItem> BuildItems(
        ResolvedMaterial material, Rectangle bounds, float scale)
    {
        var specs = ResolvedLayerSpecProjection.ParseCached(material.Layers);
        var ordered = SurfacePaintPlan.Order(specs.Specs, includeStatic: true, includeDynamic: true);
        var items = new List<MathematicalAlgorithms.Surface.SurfaceDrawItem>(ordered.Count);
        foreach (var spec in ordered)
        {
            var kind = spec.Kind switch
            {
                LayerPrimitive.Fill => SurfaceDrawKind.Fill,
                LayerPrimitive.Gradient => SurfaceDrawKind.Gradient,
                LayerPrimitive.Stroke => SurfaceDrawKind.Edge,
                LayerPrimitive.Shadow => SurfaceDrawKind.Shadow,
                LayerPrimitive.InnerShadow => SurfaceDrawKind.Edge,
                LayerPrimitive.Backdrop => SurfaceDrawKind.Fill,
                LayerPrimitive.Specular => SurfaceDrawKind.Spot,
                LayerPrimitive.Texture => SurfaceDrawKind.Texture,
                LayerPrimitive.Decal => SurfaceDrawKind.Texture,
                LayerPrimitive.Noise => SurfaceDrawKind.Noise,
                _ => SurfaceDrawKind.Fill,
            };

            if (!spec.HasColor && spec.Kind is not (LayerPrimitive.Backdrop or LayerPrimitive.Specular))
            {
                continue;
            }

            var radius = Math.Max(spec.RadiusDeclared, specs.MaterialRadius) * scale;
            var width = spec.Width * scale;
            var blur = spec.Blur * scale;
            var opacity = spec.Kind == LayerPrimitive.Specular ? 0.85f : 1f;
            items.Add(new MathematicalAlgorithms.Surface.SurfaceDrawItem(
                bounds.X, bounds.Y, bounds.Width, bounds.Height,
                radius, unchecked((uint)spec.ColorArgb), opacity, spec.Z,
                kind, SurfaceDrawFlags.Decor,
                spec.Angle, width, blur, spec.Strength));
        }

        return items;
    }

    private static Bitmap ToBitmap(EngineSurfaceFrame frame)
    {
        var bitmap = new Bitmap(frame.Width, frame.Height, PixelFormat.Format32bppPArgb);
        var data = bitmap.LockBits(
            new Rectangle(0, 0, frame.Width, frame.Height), ImageLockMode.WriteOnly, PixelFormat.Format32bppPArgb);
        try
        {
            for (var y = 0; y < frame.Height; y++)
            {
                Marshal.Copy(frame.Rgba, y * frame.Width * 4, data.Scan0 + y * data.Stride, frame.Width * 4);
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        return bitmap;
    }
}
