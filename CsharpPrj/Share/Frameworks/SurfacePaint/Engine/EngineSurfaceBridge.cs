#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.MemoryMappedFiles;
using System.Runtime.InteropServices;
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
    UiBackend.SurfaceTextureFrame? Backdrop = null);

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
            using var textureMapping = WriteTextureIfAny(request, out var textureShm, out var textureWidth, out var textureHeight);
            var json = BuildRequest(request, token, itemsShm, items.Count, textureShm, textureWidth, textureHeight);
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

    /// <summary>帧 token（同视口/同绘制项/同光/同背景 ⇒ 同帧）。</summary>
    public static string FrameTokenOf(EngineSurfaceRequest request, IReadOnlyList<SurfaceDrawItem> items)
    {
        var builder = new StringBuilder(96 + items.Count * 16);
        builder.Append(request.Width).Append('x').Append(request.Height)
            .Append('@').Append(request.Dpi)
            .Append('|').Append(request.LightToken)
            .Append('|').Append(request.Quality)
            .Append('|').Append(request.Backdrop?.Kind ?? UiBackend.SurfaceTextureKind.None)
            .Append('@').Append(request.Backdrop?.Version ?? 0L)
            .Append('@').Append(request.Backdrop?.Transport ?? string.Empty)
            .Append('@').Append((request.Backdrop?.Coverage.Width ?? 0f).ToString("F1", CultureInfo.InvariantCulture))
            .Append('x').Append((request.Backdrop?.Coverage.Height ?? 0f).ToString("F1", CultureInfo.InvariantCulture));
        foreach (var item in items)
        {
            builder.Append('|').Append(item.X.ToString("F1", CultureInfo.InvariantCulture))
                .Append(',').Append(item.Y.ToString("F1", CultureInfo.InvariantCulture))
                .Append(',').Append(item.Width.ToString("F1", CultureInfo.InvariantCulture))
                .Append(',').Append(item.Height.ToString("F1", CultureInfo.InvariantCulture))
                .Append(',').Append(item.Argb).Append(',').Append(item.Kind)
                .Append(',').Append(item.Opacity.ToString("F2", CultureInfo.InvariantCulture))
                .Append(',').Append(item.TextureId);
        }

        return SurfaceFrameContract.Fingerprint(builder.ToString());
    }

    private static string BuildRequest(
        EngineSurfaceRequest request, string token, string itemsShm, int itemCount,
        string textureShm, int textureWidth, int textureHeight)
    {
        var light = request.Light;
        var builder = new StringBuilder(512);
        builder.Append("{\"v\":2");
        builder.Append(",\"width\":").Append(request.Width);
        builder.Append(",\"height\":").Append(request.Height);
        builder.Append(",\"dpi\":").Append(request.Dpi);
        builder.Append(",\"frameToken\":\"").Append(Escape(token)).Append('"');
        builder.Append(",\"lightToken\":\"").Append(Escape(request.LightToken)).Append('"');
        builder.Append(",\"quality\":\"").Append(Escape(request.Quality)).Append('"');
        builder.Append(",\"itemsShm\":\"").Append(Escape(itemsShm)).Append('"');
        builder.Append(",\"itemCount\":").Append(itemCount);

        if (request.Backdrop is { Available: true } backdrop)
        {
            builder.Append(",\"backdrop\":{\"transport\":\"shm\",\"shm\":\"").Append(Escape(textureShm)).Append('"');
            builder.Append(",\"width\":").Append(textureWidth > 0 ? textureWidth : (int)Math.Max(1f, backdrop.Coverage.Width));
            builder.Append(",\"height\":").Append(textureHeight > 0 ? textureHeight : (int)Math.Max(1f, backdrop.Coverage.Height));
            builder.Append(",\"dpi\":").Append(backdrop.Dpi);
            builder.Append(",\"version\":").Append(backdrop.Version);
            builder.Append(",\"originX\":").Append(backdrop.OriginX.ToString("F2", CultureInfo.InvariantCulture));
            builder.Append(",\"originY\":").Append(backdrop.OriginY.ToString("F2", CultureInfo.InvariantCulture));
            builder.Append('}');
        }
        else
        {
            builder.Append(",\"backdrop\":{\"available\":false,\"reason\":\"backdrop-unavailable\"}");
        }

        builder.Append(",\"light\":{\"dirX\":").Append(light.DirectionX.ToString("F4", CultureInfo.InvariantCulture));
        builder.Append(",\"dirY\":").Append(light.DirectionY.ToString("F4", CultureInfo.InvariantCulture));
        builder.Append(",\"dirZ\":").Append(light.DirectionZ.ToString("F4", CultureInfo.InvariantCulture));
        builder.Append(",\"intensity\":").Append(light.Intensity.ToString("F4", CultureInfo.InvariantCulture));
        builder.Append(",\"ambient\":").Append(light.Ambient.ToString("F4", CultureInfo.InvariantCulture));
        builder.Append(",\"warmth\":").Append(light.Warmth.ToString("F4", CultureInfo.InvariantCulture));
        builder.Append(",\"tint\":").Append(light.TintArgb);
        builder.Append(",\"spotX\":").Append(light.SpotLocalX.ToString("F2", CultureInfo.InvariantCulture));
        builder.Append(",\"spotY\":").Append(light.SpotLocalY.ToString("F2", CultureInfo.InvariantCulture));
        builder.Append(",\"spotRadius\":").Append(light.SpotRadiusDips.ToString("F2", CultureInfo.InvariantCulture));
        builder.Append(",\"shade\":").Append(light.Shade.ToString("F3", CultureInfo.InvariantCulture));
        builder.Append("}}");
        return builder.ToString();
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

    /// <summary>
    /// 把背景位图写入共享内存（RGBA 直通）；无位图/已有 shm 名 ⇒ 不新建。
    /// <para>命名段须活到端口调用返回（由 <see cref="TryRender"/> 的 using 负责释放）。</para>
    /// </summary>
    private static MemoryMappedFile? WriteTextureIfAny(
        EngineSurfaceRequest request, out string name, out int width, out int height)
    {
        name = string.Empty;
        width = 0;
        height = 0;
        if (request.Backdrop is not { Available: true } backdrop) return null;
        if (backdrop.Content is string existing)
        {
            name = existing;
            return null;
        }

        if (backdrop.Content is not System.Drawing.Bitmap bitmap) return null;
        width = bitmap.Width;
        height = bitmap.Height;
        if (width <= 0 || height <= 0
            || width > SurfaceFrameContract.MaxDimension || height > SurfaceFrameContract.MaxDimension)
        {
            return null;
        }

        var rgba = new byte[checked(width * height * UiBackend.SurfacePixelFormat.BytesPerPixel)];
        var data = bitmap.LockBits(
            new System.Drawing.Rectangle(0, 0, width, height),
            System.Drawing.Imaging.ImageLockMode.ReadOnly,
            System.Drawing.Imaging.PixelFormat.Format32bppArgb);
        try
        {
            var row = new byte[width * 4];
            for (var y = 0; y < height; y++)
            {
                Marshal.Copy(data.Scan0 + y * data.Stride, row, 0, row.Length);
                var target = y * row.Length;
                for (var x = 0; x < row.Length; x += 4)
                {
                    rgba[target + x] = row[x + 2];
                    rgba[target + x + 1] = row[x + 1];
                    rgba[target + x + 2] = row[x];
                    rgba[target + x + 3] = row[x + 3];
                }
            }
        }
        finally
        {
            bitmap.UnlockBits(data);
        }

        name = SurfaceFrameContract.TextureShmPrefix + Environment.ProcessId + "_"
               + System.Threading.Interlocked.Increment(ref _sequence);
        var mapping = MemoryMappedFile.CreateNew(name, rgba.Length);
        using (var view = mapping.CreateViewAccessor(0, rgba.Length, MemoryMappedFileAccess.Write))
        {
            view.WriteArray(0, rgba, 0, rgba.Length);
        }

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
    {
        if (string.IsNullOrEmpty(value)) return string.Empty;
        var builder = new StringBuilder(value.Length + 8);
        foreach (var ch in value)
        {
            switch (ch)
            {
                case '\\': builder.Append("\\\\"); break;
                case '"': builder.Append("\\\""); break;
                case '\n': builder.Append("\\n"); break;
                case '\r': builder.Append("\\r"); break;
                case '\t': builder.Append("\\t"); break;
                case '\b': builder.Append("\\b"); break;
                case '\f': builder.Append("\\f"); break;
                default:
                    if (ch < 0x20)
                    {
                        builder.Append("\\u").Append(((int)ch).ToString("x4", CultureInfo.InvariantCulture));
                    }
                    else
                    {
                        builder.Append(ch);
                    }

                    break;
            }
        }

        return builder.ToString();
    }
}
