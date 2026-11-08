#nullable enable

using System;

namespace UiBackend;

/// <summary>
/// 像素格式转换（**唯一实现**）：引擎帧 = 直通 R8G8B8A8；GDI+ 位图 = 预乘 BGRA（<c>Format32bppPArgb</c>）。
/// <para>旧实现把引擎字节直接拷进 PArgb（通道与预乘都没换）⇒ 偏色 + 边缘发白；转换只在本类发生。</para>
/// </summary>
public static class SurfacePixelFormat
{
    /// <summary>每像素字节数（R8G8B8A8 / B8G8R8A8 同为 4）。</summary>
    public const int BytesPerPixel = 4;

    /// <summary>
    /// 直通 RGBA → 预乘 BGRA（一次性整帧转换）。
    /// </summary>
    /// <param name="rgba">源（R,G,B,A 顺序）。</param>
    /// <param name="bgra">目标（B,G,R,A 顺序，已预乘）；长度须 ≥ 源长度。</param>
    public static void RgbaToPremultipliedBgra(ReadOnlySpan<byte> rgba, Span<byte> bgra)
    {
        if (rgba.Length % BytesPerPixel != 0)
        {
            throw new ArgumentException("源长度必须是 4 的整数倍（R8G8B8A8）", nameof(rgba));
        }

        if (bgra.Length < rgba.Length)
        {
            throw new ArgumentException("目标缓冲区小于源（需等长）", nameof(bgra));
        }

        for (var i = 0; i + 3 < rgba.Length; i += BytesPerPixel)
        {
            var a = rgba[i + 3];
            bgra[i] = Premultiply(rgba[i + 2], a);
            bgra[i + 1] = Premultiply(rgba[i + 1], a);
            bgra[i + 2] = Premultiply(rgba[i], a);
            bgra[i + 3] = a;
        }
    }

    /// <summary>直通 RGBA → 预乘 RGBA（引擎内部需要预乘通道时使用）。</summary>
    /// <param name="rgba">源/目标（就地可传同一缓冲区）。</param>
    public static void StraightToPremultiplied(ReadOnlySpan<byte> rgba, Span<byte> target)
    {
        if (target.Length < rgba.Length)
        {
            throw new ArgumentException("目标缓冲区小于源（需等长）", nameof(target));
        }

        for (var i = 0; i + 3 < rgba.Length; i += BytesPerPixel)
        {
            var a = rgba[i + 3];
            target[i] = Premultiply(rgba[i], a);
            target[i + 1] = Premultiply(rgba[i + 1], a);
            target[i + 2] = Premultiply(rgba[i + 2], a);
            target[i + 3] = a;
        }
    }

    /// <summary>单通道预乘（四舍五入；α=0 ⇒ 0）。</summary>
    /// <param name="value">直通通道值（0..255）。</param>
    /// <param name="alpha">α（0..255）。</param>
    public static byte Premultiply(byte value, byte alpha)
        => alpha == 0 ? (byte)0 : (byte)((value * alpha + 127) / 255);

    /// <summary>分配整帧目标缓冲（预乘 BGRA）。</summary>
    /// <param name="width">宽（像素）。</param>
    /// <param name="height">高（像素）。</param>
    public static byte[] AllocatePremultipliedBgra(int width, int height)
    {
        if (width <= 0) throw new ArgumentOutOfRangeException(nameof(width));
        if (height <= 0) throw new ArgumentOutOfRangeException(nameof(height));
        return new byte[checked(width * height * BytesPerPixel)];
    }
}
