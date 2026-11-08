#nullable enable

using System.Runtime.InteropServices;

namespace MathematicalAlgorithms.Surface;

/// <summary>
/// UI 绘制项（Engine 与 Frame 共享的唯一数据面布局）。
/// <para>字段全部 unmanaged；大小由 <see cref="SurfaceFrameContract.ItemSize"/> 断言（56 字节）。</para>
/// <para>光照不进入 <see cref="Argb"/>：着色在引擎像素阶段完成（基色 × 光照）。</para>
/// </summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public readonly struct SurfaceDrawItem
{
    /// <summary>设备像素 X。</summary>
    public readonly float X;

    /// <summary>设备像素 Y。</summary>
    public readonly float Y;

    /// <summary>设备像素宽。</summary>
    public readonly float Width;

    /// <summary>设备像素高。</summary>
    public readonly float Height;

    /// <summary>圆角（设备像素，已按 DPI 换算）。</summary>
    public readonly float Radius;

    /// <summary>基色（0xAARRGGBB；不含光照）。</summary>
    public readonly uint Argb;

    /// <summary>不透明度（0..1）。</summary>
    public readonly float Opacity;

    /// <summary>层序（升序落笔）。</summary>
    public readonly float Z;

    /// <summary>图元（见 <see cref="SurfaceDrawKind"/>）。</summary>
    public readonly ushort Kind;

    /// <summary>标志位（见 <see cref="SurfaceDrawFlags"/>）。</summary>
    public readonly ushort Flags;

    /// <summary>参数 0（渐变角/描边宽/模糊半径/相位）。</summary>
    public readonly float Param0;

    /// <summary>参数 1。</summary>
    public readonly float Param1;

    /// <summary>参数 2。</summary>
    public readonly float Param2;

    /// <summary>参数 3。</summary>
    public readonly float Param3;

    /// <summary>贴图 id（0 = 无贴图）。</summary>
    public readonly int TextureId;

    /// <summary>构造绘制项。</summary>
    public SurfaceDrawItem(
        float x, float y, float width, float height, float radius, uint argb, float opacity, float z,
        ushort kind, ushort flags,
        float param0 = 0f, float param1 = 0f, float param2 = 0f, float param3 = 0f, int textureId = 0)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
        Radius = radius;
        Argb = argb;
        Opacity = opacity;
        Z = z;
        Kind = kind;
        Flags = flags;
        Param0 = param0;
        Param1 = param1;
        Param2 = param2;
        Param3 = param3;
        TextureId = textureId;
    }
}

/// <summary>绘制项图元词表（与 SurfaceShader 的 Kind 分支一一对应）。</summary>
public static class SurfaceDrawKind
{
    public const ushort Fill = 0;
    public const ushort Gradient = 1;
    public const ushort Edge = 2;
    public const ushort Ring = 3;
    public const ushort Spot = 4;
    public const ushort Texture = 5;
    public const ushort Shadow = 6;
    public const ushort Noise = 7;

    /// <summary>背后内容（真玻璃基底；旧实现被压平成 <see cref="Fill"/> 而丢失模糊语义）。</summary>
    public const ushort Backdrop = 8;
}

/// <summary>绘制项标志位。</summary>
public static class SurfaceDrawFlags
{
    public const ushort None = 0;
    public const ushort Clip = 1;
    public const ushort Blend = 2;
    public const ushort Decor = 4;
}
