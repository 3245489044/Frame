#nullable enable

using System;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace MathematicalAlgorithms.Surface;

/// <summary>
/// UI 帧契约（Engine 与 Frame 唯一事实源）：magic/version/头布局/上限/共享内存命名。
/// <para>两侧启动自检：<see cref="Validate"/> 失败即 fail-fast（禁静默使用旧帧）。</para>
/// </summary>
public static class SurfaceFrameContract
{
    /// <summary>绘制项汇聚面 magic（'WFSD'）。</summary>
    public const uint DrawMagic = 0x44534657;

    /// <summary>帧输出面 magic（'WFSF'）。</summary>
    public const uint FrameMagic = 0x46534657;

    /// <summary>契约版本（v2：请求新增 backdrop 段；不匹配 ⇒ contract-version-mismatch）。</summary>
    public const int Version = 2;

    /// <summary>绘制头部字节数。</summary>
    public const int HeaderSize = 64;

    /// <summary>单帧绘制项上限。</summary>
    public const int MaxItems = 8192;

    /// <summary>单边像素上限。</summary>
    public const int MaxDimension = 8192;

    /// <summary>绘制项字节数（布局断言的唯一数值）。</summary>
    public const int ItemSize = 56;

    /// <summary>绘制项共享内存前缀。</summary>
    public const string DrawShmPrefix = "Local\\WF_SurfaceDraw_";

    /// <summary>帧输出共享内存前缀。</summary>
    public const string FrameShmPrefix = "Local\\WF_SurfaceFrame_";

    /// <summary>贴图共享内存前缀（预留）。</summary>
    public const string TextureShmPrefix = "Local\\WF_SurfaceTexture_";

    /// <summary>启动自检：布局/常量一致才允许运行。</summary>
    public static void Validate()
    {
        var actual = Marshal.SizeOf<SurfaceDrawItem>();
        if (actual != ItemSize)
        {
            throw new InvalidOperationException(
                $"SurfaceDrawItem 布局漂移：期望 {ItemSize} 字节，实际 {actual} 字节");
        }

        if (Marshal.SizeOf<SurfaceDrawHeader>() != HeaderSize)
        {
            throw new InvalidOperationException(
                $"SurfaceDrawHeader 布局漂移：期望 {HeaderSize} 字节，实际 {Marshal.SizeOf<SurfaceDrawHeader>()} 字节");
        }

        if (Marshal.SizeOf<SurfaceFrameHeader>() != HeaderSize)
        {
            throw new InvalidOperationException(
                $"SurfaceFrameHeader 布局漂移：期望 {HeaderSize} 字节，实际 {Marshal.SizeOf<SurfaceFrameHeader>()} 字节");
        }
    }

    /// <summary>内容指纹（SHA256，十六进制小写）。</summary>
    public static string Fingerprint(ReadOnlySpan<byte> payload)
        => Convert.ToHexString(SHA256.HashData(payload)).ToLowerInvariant();

    /// <summary>文本指纹（UTF8）。</summary>
    public static string Fingerprint(string text)
        => Fingerprint(Encoding.UTF8.GetBytes(text ?? string.Empty));

    /// <summary>帧 token 文件名安全化（共享内存名只含字母数字与下划线）。</summary>
    public static string SafeToken(string token)
    {
        var builder = new StringBuilder(token.Length);
        foreach (var ch in token)
        {
            builder.Append(char.IsLetterOrDigit(ch) ? ch : '_');
        }

        return builder.Length == 0 ? "empty" : builder.ToString();
    }
}

/// <summary>绘制项共享内存头部（64 字节）。</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct SurfaceDrawHeader
{
    public uint Magic;
    public ushort Version;
    public ushort ItemSize;
    public int Count;
    public int Width;
    public int Height;
    public int Dpi;
    public int Reserved;
    public int Reserved1;
    public ulong TokenA;
    public ulong TokenB;
    public ulong Reserved2;
    public ulong Reserved3;
}

/// <summary>帧输出共享内存头部（64 字节；像素紧随其后）。</summary>
[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct SurfaceFrameHeader
{
    public uint Magic;
    public ushort Version;
    public ushort Reserved;
    public int Width;
    public int Height;
    public int Stride;
    public int Length;
    public int Reserved2;
    public int Reserved3;
    public ulong TokenA;
    public ulong TokenB;
    public ulong Reserved4;
    public ulong Reserved5;
}
