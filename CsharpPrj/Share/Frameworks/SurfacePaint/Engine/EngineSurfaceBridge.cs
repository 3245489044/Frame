#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Text;
using System.Text.Json;
using MathematicalAlgorithms.Surface;

namespace Share.SurfacePaint.Engine;

/// <summary>宿主侧一帧请求（绘制项已由材质声明生成）。</summary>
public sealed record EngineSurfaceRequest(
    int Width, int Height, int Dpi,
    string LightToken, string Quality,
    SurfaceFrameLightData Light,
    IReadOnlyList<SurfaceDrawItem> Items,
    UiBackend.SurfaceBackdropFrame? Backdrop = null);

/// <summary>整帧光照参数（与引擎契约同序）。</summary>
public readonly record struct SurfaceFrameLightData(
    float DirectionX, float DirectionY, float DirectionZ,
    float Intensity, float Ambient, float Warmth, int TintArgb,
    float SpotLocalX, float SpotLocalY, float SpotRadiusDips, float Shade)
{
    public static readonly SurfaceFrameLightData Default = new(
        0f, 0f, 1f, 0.85f, 0.18f, 0.12f, unchecked((int)0xFFDCEBFF), 0f, 0f, 64f, 1f);
}

/// <summary>引擎返回的一帧（像素已从共享内存取出）。</summary>
public sealed record EngineSurfaceFrame(
    int Width, int Height, int Stride, byte[] Rgba,
    string FrameToken, string LightToken,
    double GpuMs, double ReadbackMs, string Reason)
{
    public bool HasPixels => Rgba.Length > 0;
}

/// <summary>跨 ALC 端口桥：控制面 JSON + 数据面共享内存。</summary>
public static class EngineSurfaceBridge
{
    private static int _sequence;

    /// <summary>渲染一帧；端口缺失/失败 ⇒ null（调用方必须可见降级）。</summary>
    public static EngineSurfaceFrame? TryRender(
        Func<string, string>? port, EngineSurfaceRequest request, out string reason)
    {
        reason = string.Empty;
        if (port is null)
        {
            reason = "port-missing";
            return null;
        }

        SurfaceFrameContract.Validate();
        var items = request.Items ?? Array.Empty<SurfaceDrawItem>();
        if (items.Count > SurfaceFrameContract.MaxItems)
        {
            reason = "item-overflow";
            return null;
        }

        var token = FrameTokenOf(request, items);
        try
        {
            using var itemsMapping = WriteItems(items, out var itemsShm);
            var json = BuildRequest(request, token, itemsShm, items.Count);
            string? response;
            try
            {
                response = port(json);
            }
            catch (Exception ex)
            {
                // 端口调用本身抛出（跨 ALC 类型/序列化等）：与「响应不可解析」分开归因。
                reason = "port-call-exception:" + ex.GetType().Name;
                return null;
            }

            if (string.IsNullOrWhiteSpace(response))
            {
                reason = "port-empty";
                return null;
            }

            using var document = JsonDocument.Parse(response);
            var root = document.RootElement;
            var ok = !root.TryGetProperty("ok", out var okElement) || okElement.ValueKind != JsonValueKind.False;
            var degraded = GetString(root, "degraded");
            var responseReason = GetString(root, "reason");
            var pixelsShm = ReadPixelsShm(root);
            if (!ok || !string.IsNullOrEmpty(degraded) || string.IsNullOrEmpty(pixelsShm))
            {
                reason = string.IsNullOrEmpty(degraded)
                    ? (string.IsNullOrEmpty(responseReason) ? "no-pixels" : responseReason)
                    : degraded;
                return null;
            }

            var rgba = ReadPixels(pixelsShm);
            if (rgba is null || rgba.Length == 0)
            {
                reason = "pixels-unreadable";
                return null;
            }

            return new EngineSurfaceFrame(
                GetInt(root, "width", request.Width),
                GetInt(root, "height", request.Height),
                GetInt(root, "stride", request.Width * 4),
                rgba,
                GetString(root, "frameToken"),
                GetString(root, "lightToken"),
                GetFloat(root, "gpuMs"),
                GetFloat(root, "readbackMs"),
                string.Empty);
        }
        catch (Exception ex)
        {
            reason = "response-parse-error:" + ex.GetType().Name;
            return null;
        }
    }

    /// <summary>帧 token（同视口/同绘制项/同光 ⇒ 同帧）。</summary>
    public static string FrameTokenOf(EngineSurfaceRequest request, IReadOnlyList<SurfaceDrawItem> items)
    {
        var builder = new StringBuilder(64 + items.Count * 16);
        builder.Append(request.Width).Append('x').Append(request.Height)
            .Append('@').Append(request.Dpi)
            .Append('|').Append(request.LightToken)
            .Append('|').Append(request.Quality)
            .Append('|').Append(request.Backdrop?.Version ?? 0L);
        foreach (var item in items)
        {
            builder.Append('|').Append(item.X.ToString("F1", CultureInfo.InvariantCulture))
                .Append(',').Append(item.Y.ToString("F1", CultureInfo.InvariantCulture))
                .Append(',').Append(item.Width.ToString("F1", CultureInfo.InvariantCulture))
                .Append(',').Append(item.Height.ToString("F1", CultureInfo.InvariantCulture))
                .Append(',').Append(item.Argb).Append(',').Append(item.Kind)
                .Append(',').Append(item.Opacity.ToString("F2", CultureInfo.InvariantCulture));
        }

        return SurfaceFrameContract.Fingerprint(builder.ToString());
    }

    private static string BuildRequest(
        EngineSurfaceRequest request, string token, string itemsShm, int itemCount)
    {
        var light = request.Light;
        var backdrop = request.Backdrop is { Available: true } frame
            ? string.Create(CultureInfo.InvariantCulture,
                $"\"backdrop\":{{\"shm\":\"{Escape(frame.Handle as string)}\",\"width\":{(int)Math.Max(1f, frame.Coverage.Width)},\"height\":{(int)Math.Max(1f, frame.Coverage.Height)},\"dpi\":{frame.Dpi},\"version\":{frame.Version},\"originX\":{frame.OriginX:F2},\"originY\":{frame.OriginY:F2}}},")
            : "\"backdrop\":{\"available\":false,\"reason\":\"backdrop-unavailable\"},";
        var head = string.Empty + (string)(
            $"{{\"v\":2,\"width\":{request.Width},\"height\":{request.Height},\"dpi\":{request.Dpi},"
            + $"\"frameToken\":\"{token}\",\"lightToken\":\"{Escape(request.LightToken)}\",\""
            + $"\"quality\":\"{Escape(request.Quality)}\",\"itemsShm\":\"{Escape(itemsShm)}\",\""
            + $"\"itemCount\":{itemCount},"
        );

            var backdropPart = backdrop;
            var tail = string.Empty + (string)(

            string.Empty

            + $"\"light\":{{\"dirX\":{light.DirectionX:F4},\"dirY\":{light.DirectionY:F4},\"dirZ\":{light.DirectionZ:F4},\""
            + $"\"intensity\":{light.Intensity:F4},\"ambient\":{light.Ambient:F4},\"warmth\":{light.Warmth:F4},\""
            + $"\"tint\":{light.TintArgb},\"spotX\":{light.SpotLocalX:F2},\"spotY\":{light.SpotLocalY:F2},\""
            + $"\"spotRadius\":{light.SpotRadiusDips:F2},\"shade\":{light.Shade:F3}}}}}}}");
        return head + backdropPart + tail;

    }

    private static MemoryMappedFile WriteItems(IReadOnlyList<SurfaceDrawItem> items, out string name)
    {
        name = SurfaceFrameContract.DrawShmPrefix + Environment.ProcessId + "_"
               + System.Threading.Interlocked.Increment(ref _sequence);
        var length = SurfaceFrameContract.HeaderSize + items.Count * SurfaceFrameContract.ItemSize;
        var mapping = MemoryMappedFile.CreateNew(name, length);
        using (var view = mapping.CreateViewAccessor(0, length, MemoryMappedFileAccess.Write))
        {
            var header = new SurfaceDrawHeader
            {
                Magic = SurfaceFrameContract.DrawMagic,
                Version = SurfaceFrameContract.Version,
                ItemSize = SurfaceFrameContract.ItemSize,
                Count = items.Count,
                Dpi = 96,
            };
            view.Write(0, ref header);
            if (items.Count > 0)
            {
                var array = new SurfaceDrawItem[items.Count];
                for (var i = 0; i < items.Count; i++) array[i] = items[i];
                view.WriteArray(SurfaceFrameContract.HeaderSize, array, 0, array.Length);
            }
        }

        // 命名段必须活到端口调用返回；using 在 TryRender 内释放。
        return mapping;
    }

    private static byte[]? ReadPixels(string name)
    {
        using var mapping = MemoryMappedFile.OpenExisting(name, MemoryMappedFileRights.Read);
        using var view = mapping.CreateViewAccessor(0, 0, MemoryMappedFileAccess.Read);
        view.Read(0, out SurfaceFrameHeader header);
        if (header.Magic != SurfaceFrameContract.FrameMagic
            || header.Version != SurfaceFrameContract.Version
            || header.Length <= 0)
        {
            return null;
        }

        var pixels = new byte[header.Length];
        view.ReadArray(SurfaceFrameContract.HeaderSize, pixels, 0, pixels.Length);
        return pixels;
    }

    private static string ReadPixelsShm(JsonElement root)
    {
        if (!root.TryGetProperty("pixels", out var pixels) || pixels.ValueKind != JsonValueKind.Object)
        {
            return string.Empty;
        }

        return GetString(pixels, "shm");
    }

    private static string GetString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString() ?? string.Empty : string.Empty;

    private static int GetInt(JsonElement element, string name, int fallback)
        => element.TryGetProperty(name, out var value) && value.TryGetInt32(out var parsed) ? parsed : fallback;

    private static float GetFloat(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.TryGetSingle(out var parsed) ? parsed : 0f;

    private static string Escape(string? value)
        => (value ?? string.Empty).Replace("\\", "\\\\").Replace("\"", "\\\"", StringComparison.Ordinal);
}
