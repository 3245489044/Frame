#nullable enable

using System;
using System.Threading;

namespace UiBackend;

/// <summary>
/// 全局光源方向（**指向光**的单位向量；屏幕空间约定：x 向右、y 向下、z 指向观察者）。
/// <para>全帧唯一的太阳方向是 <see cref="SurfaceLightFrame.Sun"/>；组件采样出的方向是同一光场在该位置的插值结果。</para>
/// </summary>
public readonly record struct SurfaceLightDirection(float X, float Y, float Z)
{
    /// <summary>零向量（<see cref="SurfaceLightSample.HasValue"/> 为 false 时使用）。</summary>
    public static readonly SurfaceLightDirection Empty = new(0f, 0f, 0f);

    /// <summary>长度（用于判空与归一化）。</summary>
    public float Length => MathF.Sqrt(X * X + Y * Y + Z * Z);

    /// <summary>单位化；零向量返回 <see cref="Empty"/>（调用方必须处理空方向）。</summary>
    public SurfaceLightDirection Normalized()
    {
        var length = Length;
        return length <= 1e-6f ? Empty : new SurfaceLightDirection(X / length, Y / length, Z / length);
    }
}

/// <summary>帧覆盖的 UI 空间（屏幕 DIP；x 向右、y 向下）。</summary>
public readonly record struct SurfaceLightCoverage(float X, float Y, float Width, float Height)
{
    /// <summary>单点覆盖退化保护（宽/高 ≤0 时按中心点求值）。</summary>
    public float UnitX(float x) => Width <= 1e-6f ? 0.5f : Math.Clamp((x - X) / Width, 0f, 1f);

    /// <inheritdoc cref="UnitX"/>
    public float UnitY(float y) => Height <= 1e-6f ? 0.5f : Math.Clamp((y - Y) / Height, 0f, 1f);
}

/// <summary>组件探针：只描述「在界面上的哪一块」，不含任何光参数。</summary>
public readonly record struct SurfaceLightProbe(
    float X,
    float Y,
    float Width,
    float Height,
    int ZOrder = 0,
    float Dpi = 96f)
{
    /// <summary>矩形中心 X（DIP）。</summary>
    public float CenterX => X + Width * 0.5f;

    /// <summary>矩形中心 Y（DIP）。</summary>
    public float CenterY => Y + Height * 0.5f;
}

/// <summary>光场单元（Engine 侧由虚拟矩阵求值填入；UI 侧只读）。</summary>
public readonly record struct SurfaceLightCell(
    float DirX,
    float DirY,
    float DirZ,
    float Specular,
    float Transmission,
    int TintArgb);

/// <summary>
/// 一帧全局光场（不可变快照）。全帧只有一份 <see cref="Sun"/>/<see cref="SunIntensity"/>/<see cref="Ambient"/>/
/// <see cref="Warmth"/>/<see cref="TintArgb"/>；<see cref="Cells"/> 是矩阵在 UI 切片上的局部调制。
/// <para>发布语义：整帧替换（见 <see cref="SurfaceLightRegistry"/>），读侧不加锁。</para>
/// </summary>
public sealed class SurfaceLightFrame
{
    /// <summary>构造一帧光场。</summary>
    /// <param name="signature">合成签名（世界 ⊕ 配置 ⊕ 时间桶）；用于一切缓存键。</param>
    /// <param name="worldSignature">虚拟矩阵声明/生成器 ⊕ 采样区版本。</param>
    /// <param name="configSignature">映射/网格/太阳参数哈希。</param>
    /// <param name="epoch">连续时间桶（秒级，**不是** 15 分钟量化）。</param>
    /// <param name="coverage">本帧覆盖的 UI 空间（屏幕 DIP）。</param>
    /// <param name="columns">光场列数（≥1）。</param>
    /// <param name="rows">光场行数（≥1）。</param>
    /// <param name="sun">全帧唯一太阳方向（指向光）。</param>
    /// <param name="sunIntensity">全局日照强度（0..1.25）。</param>
    /// <param name="ambient">全局环境强度（0..1）。</param>
    /// <param name="warmth">全局色温（0..1）。</param>
    /// <param name="tintArgb">全局环境底色（0xAARRGGBB）。</param>
    /// <param name="cells">行主序光场单元；数量必须等于 <paramref name="columns"/>×<paramref name="rows"/>。</param>
    public SurfaceLightFrame(
        long signature,
        long worldSignature,
        long configSignature,
        long epoch,
        SurfaceLightCoverage coverage,
        int columns,
        int rows,
        SurfaceLightDirection sun,
        float sunIntensity,
        float ambient,
        float warmth,
        int tintArgb,
        SurfaceLightCell[] cells)
    {
        if (columns <= 0 || rows <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(columns), "光场网格必须 ≥1×1。");
        }

        if (cells is null || cells.Length != checked(columns * rows))
        {
            throw new ArgumentException("光场单元数量必须等于列数×行数。", nameof(cells));
        }

        Signature = signature;
        WorldSignature = worldSignature;
        ConfigSignature = configSignature;
        Epoch = epoch;
        Coverage = coverage;
        Columns = columns;
        Rows = rows;
        Sun = sun.Normalized();
        SunIntensity = sunIntensity;
        Ambient = ambient;
        Warmth = warmth;
        TintArgb = tintArgb;
        Cells = cells;
    }

    /// <summary>合成签名（世界 ⊕ 配置 ⊕ 时间桶）。</summary>
    public long Signature { get; }

    /// <summary>虚拟矩阵签名（声明/生成器 ⊕ 采样区版本）。</summary>
    public long WorldSignature { get; }

    /// <summary>光场配置签名（映射/网格/太阳参数）。</summary>
    public long ConfigSignature { get; }

    /// <summary>连续时间桶（秒级）。</summary>
    public long Epoch { get; }

    /// <summary>帧覆盖的 UI 空间（屏幕 DIP）。</summary>
    public SurfaceLightCoverage Coverage { get; }

    /// <summary>列数。</summary>
    public int Columns { get; }

    /// <summary>行数。</summary>
    public int Rows { get; }

    /// <summary>全帧唯一太阳方向（指向光）。</summary>
    public SurfaceLightDirection Sun { get; }

    /// <summary>全局日照强度。</summary>
    public float SunIntensity { get; }

    /// <summary>全局环境强度。</summary>
    public float Ambient { get; }

    /// <summary>全局色温（0..1）。</summary>
    public float Warmth { get; }

    /// <summary>全局环境底色（0xAARRGGBB）。</summary>
    public int TintArgb { get; }

    /// <summary>行主序光场单元（只读视图；实现内部不再改写）。</summary>
    public SurfaceLightCell[] Cells { get; }

    /// <summary>按网格坐标取单元（越界 clamp）。</summary>
    public SurfaceLightCell CellAt(int column, int row)
    {
        var cx = Math.Clamp(column, 0, Columns - 1);
        var cy = Math.Clamp(row, 0, Rows - 1);
        return Cells[cy * Columns + cx];
    }
}

/// <summary>
/// 组件光态（端口输出）：只描述「这一处」的光，不携带任何求解过程。
/// <para>字段与现有载体参数一一对应：<see cref="Angle"/> → specular 角度；<see cref="EdgeTop"/> 等 → 边缘光；<see cref="ShadowX"/> 等 → 阴影方向。</para>
/// </summary>
public readonly record struct SurfaceLightSample(
    bool HasValue,
    SurfaceLightDirection Direction,
    float Angle,
    float Intensity,
    float Warmth,
    float SpotX,
    float SpotY,
    float EdgeTop,
    float EdgeLeft,
    float EdgeRight,
    float EdgeBottom,
    float ShadowX,
    float ShadowY,
    int TintArgb,
    long Signature,
    string CacheToken)
{
    /// <summary>无光场可用时的空值：载体保持声明值并记账，**不得**回落到时间表近似。</summary>
    public static readonly SurfaceLightSample Unavailable = new(
        HasValue: false,
        Direction: SurfaceLightDirection.Empty,
        Angle: 135f,
        Intensity: 0f,
        Warmth: 0f,
        SpotX: 0.5f,
        SpotY: 0.5f,
        EdgeTop: 0f,
        EdgeLeft: 0f,
        EdgeRight: 0f,
        EdgeBottom: 0f,
        ShadowX: 0f,
        ShadowY: 0f,
        TintArgb: unchecked((int)0xFFDCEBFF),
        Signature: 0,
        CacheToken: "light-unavailable");
}

/// <summary>
/// 唯一采样器（**全仓唯一的光场插值实现**）：帧 + 组件矩形 → 组件光态。
/// <para>不做太阳/环境求值，不做时间近似，不按控件解算；两个载体必须共用本类。</para>
/// </summary>
public static class SurfaceLightPort
{
    /// <summary>按组件矩形采样一帧光场。</summary>
    /// <param name="frame">全局光场快照；null ⇒ <see cref="SurfaceLightSample.Unavailable"/>。</param>
    /// <param name="probe">组件在界面上的矩形（屏幕 DIP）。</param>
    public static SurfaceLightSample Sample(SurfaceLightFrame? frame, in SurfaceLightProbe probe)
    {
        if (frame is null) return SurfaceLightSample.Unavailable;

        var u = frame.Coverage.UnitX(probe.CenterX);
        var v = frame.Coverage.UnitY(probe.CenterY);

        var fx = u * (frame.Columns - 1);
        var fy = v * (frame.Rows - 1);
        var x0 = Math.Clamp((int)MathF.Floor(fx), 0, frame.Columns - 1);
        var y0 = Math.Clamp((int)MathF.Floor(fy), 0, frame.Rows - 1);
        var x1 = Math.Min(x0 + 1, frame.Columns - 1);
        var y1 = Math.Min(y0 + 1, frame.Rows - 1);
        var tx = fx - x0;
        var ty = fy - y0;

        var c00 = frame.CellAt(x0, y0);
        var c10 = frame.CellAt(x1, y0);
        var c01 = frame.CellAt(x0, y1);
        var c11 = frame.CellAt(x1, y1);

        var dirX = Bilinear(c00.DirX, c10.DirX, c01.DirX, c11.DirX, tx, ty);
        var dirY = Bilinear(c00.DirY, c10.DirY, c01.DirY, c11.DirY, tx, ty);
        var dirZ = Bilinear(c00.DirZ, c10.DirZ, c01.DirZ, c11.DirZ, tx, ty);
        var specular = Math.Clamp(Bilinear(c00.Specular, c10.Specular, c01.Specular, c11.Specular, tx, ty), 0f, 1f);
        var transmission = Math.Clamp(Bilinear(c00.Transmission, c10.Transmission, c01.Transmission, c11.Transmission, tx, ty), 0f, 1f);

        var direction = new SurfaceLightDirection(dirX, dirY, dirZ).Normalized();
        if (direction == SurfaceLightDirection.Empty) direction = frame.Sun;

        var intensity = Math.Clamp(
            frame.SunIntensity * (0.35f + 0.65f * specular) + frame.Ambient * 0.15f,
            0.15f, 1.25f);
        var warmth = Math.Clamp(frame.Warmth + (1f - transmission) * 0.15f, 0f, 1f);

        var spotX = Math.Clamp(0.5f + direction.X * 0.26f, 0.05f, 0.95f);
        var spotY = Math.Clamp(0.5f - direction.Y * 0.20f, 0.05f, 0.95f);

        var edgeTop = Math.Max(0f, -direction.Y);
        var edgeBottom = Math.Max(0f, direction.Y);
        var edgeLeft = Math.Max(0f, -direction.X);
        var edgeRight = Math.Max(0f, direction.X);
        var edgePeak = Math.Max(Math.Max(edgeTop, edgeBottom), Math.Max(edgeLeft, edgeRight));
        if (edgePeak > 1e-6f)
        {
            edgeTop /= edgePeak;
            edgeBottom /= edgePeak;
            edgeLeft /= edgePeak;
            edgeRight /= edgePeak;
        }

        var planar = MathF.Sqrt(direction.X * direction.X + direction.Y * direction.Y);
        var shadowX = planar > 1e-6f ? -direction.X / planar : 0f;
        var shadowY = planar > 1e-6f ? -direction.Y / planar : 0f;

        var tint = BlendArgb(
            AverageArgb(c00.TintArgb, frame.TintArgb),
            AverageArgb(c10.TintArgb, frame.TintArgb),
            AverageArgb(c01.TintArgb, frame.TintArgb),
            AverageArgb(c11.TintArgb, frame.TintArgb),
            tx, ty);

        var angle = AngleOf(direction);
        // 缓存键用**稳定签名**（世界 ⊕ 配置，不含秒桶）并量化到肉眼可辨粒度：
        // 太阳秒级移动不应驱动整窗静态底图重绘。
        var stableSignature = frame.WorldSignature ^ frame.ConfigSignature;
        var token = string.Create(
            System.Globalization.CultureInfo.InvariantCulture,
            $"S{stableSignature}:{angle:F0}:{intensity:F2}:{warmth:F2}:{spotX:F2}:{spotY:F2}:{probe.ZOrder}");

        return new SurfaceLightSample(
            HasValue: true,
            Direction: direction,
            Angle: angle,
            Intensity: intensity,
            Warmth: warmth,
            SpotX: spotX,
            SpotY: spotY,
            EdgeTop: edgeTop,
            EdgeLeft: edgeLeft,
            EdgeRight: edgeRight,
            EdgeBottom: edgeBottom,
            ShadowX: shadowX,
            ShadowY: shadowY,
            TintArgb: tint,
            Signature: stableSignature,
            CacheToken: token);
    }

    /// <summary>方向 → 角度（度；0°=右、90°=上，与现有 <c>SpotCenter</c> 口径一致）。</summary>
    public static float AngleOf(SurfaceLightDirection direction)
    {
        var degrees = MathF.Atan2(-direction.Y, direction.X) * (180f / MathF.PI);
        return (degrees % 360f + 360f) % 360f;
    }

    private static float Bilinear(float v00, float v10, float v01, float v11, float tx, float ty)
    {
        var top = v00 + (v10 - v00) * tx;
        var bottom = v01 + (v11 - v01) * tx;
        return top + (bottom - top) * ty;
    }

    private static int BlendArgb(int c00, int c10, int c01, int c11, float tx, float ty)
    {
        var (a, r, g, b) = (Average(c00, c10, c01, c11, tx, ty, 24), Average(c00, c10, c01, c11, tx, ty, 16),
            Average(c00, c10, c01, c11, tx, ty, 8), Average(c00, c10, c01, c11, tx, ty, 0));
        return unchecked((a << 24) | (r << 16) | (g << 8) | b);
    }

    private static int AverageArgb(int left, int right)
    {
        var a = (((left >> 24) & 0xFF) + ((right >> 24) & 0xFF)) / 2;
        var r = (((left >> 16) & 0xFF) + ((right >> 16) & 0xFF)) / 2;
        var g = (((left >> 8) & 0xFF) + ((right >> 8) & 0xFF)) / 2;
        var b = ((left & 0xFF) + (right & 0xFF)) / 2;
        return unchecked((a << 24) | (r << 16) | (g << 8) | b);
    }

    private static int Average(int c00, int c10, int c01, int c11, float tx, float ty, int shift)
    {
        var v00 = (c00 >> shift) & 0xFF;
        var v10 = (c10 >> shift) & 0xFF;
        var v01 = (c01 >> shift) & 0xFF;
        var v11 = (c11 >> shift) & 0xFF;
        return (int)Math.Clamp(Bilinear((float)v00, (float)v10, (float)v01, (float)v11, tx, ty), 0f, 255f);
    }
}

/// <summary>
/// 进程级光场发布点（整帧替换、读侧无锁）。只允许**一个生产者**（通道泵）发布；
/// 两个载体读同一份 <see cref="Current"/>，不得各自生成帧。
/// </summary>
public static class SurfaceLightRegistry
{
    private static volatile SurfaceLightFrame? _current;
    private static long _revision;

    /// <summary>当前帧（null ⇒ 光源不可用）。</summary>
    public static SurfaceLightFrame? Current => _current;

    /// <summary>发布代数（只增不减；用于触发重绘/缓存失效）。</summary>
    public static long Revision => Interlocked.Read(ref _revision);

    /// <summary>整帧替换（同一 <see cref="SurfaceLightFrame.Signature"/> 可重复发布，视为幂等）。</summary>
    public static void Publish(SurfaceLightFrame? frame)
    {
        _current = frame;
        Interlocked.Increment(ref _revision);
    }

    /// <summary>清空（宿主切换/引擎退出）。</summary>
    public static void Clear() => Publish(null);
}
