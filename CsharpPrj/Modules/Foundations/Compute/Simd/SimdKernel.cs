using System.Numerics;

namespace Modules.Compute.Simd;

/// <summary>
/// 具名 SIMD 核（不是通用委托：委托会阻断内联与向量化）。
/// <ul>
///   <li>每个核：<c>Vector&lt;float&gt;</c> 主体 + 标量尾，逐 lane 复现<b>同一运算顺序</b>（乘法后加法，不合 FMA、不重结合）。</li>
///   <li>A 档（逐位一致）：这些核只做「逐 lane 精确 IEEE 运算」，与标量尾逐位同值；跨架构风险另见 <see cref="SimdGuards"/>。</li>
///   <li>整数哈希（<c>Hash2D/Hash3D</c>）不在这里向量化：每 lattice 单元只算 8 次，不进入体素循环。</li>
/// </ul>
/// </summary>
public static class SimdKernel
{
    /// <summary>当前机器的 <c>Vector&lt;float&gt;</c> 宽度（8 = AVX2，4 = SSE，16 = AVX-512）。</summary>
    public static int Width => Vector<float>.Count;

    /// <summary><c>dst = a + (b - a) * t</c>（逐点链，顺序与标量完全一致）。</summary>
    public static void Lerp(ReadOnlySpan<float> a, ReadOnlySpan<float> b, ReadOnlySpan<float> t, Span<float> dst)
    {
        if (a.Length != b.Length || a.Length != t.Length || a.Length != dst.Length)
            throw new ArgumentException("Lerp 的 a/b/t/dst 长度必须一致");

        int w = Vector<float>.Count;
        int i = 0;
        for (; i + w <= a.Length; i += w)
        {
            var va = new Vector<float>(a.Slice(i));
            var vb = new Vector<float>(b.Slice(i));
            var vt = new Vector<float>(t.Slice(i));
            (va + (vb - va) * vt).CopyTo(dst.Slice(i));
        }
        for (; i < a.Length; i++)
            dst[i] = a[i] + (b[i] - a[i]) * t[i];
    }

    /// <summary>
    /// 双线性（二维值噪声的体素复现）：<c>a = c00 + (c10-c00)*u; b = c01 + (c11-c01)*u; r = a + (b-a)*v</c>。
    /// <para>与 <c>TerrainNoise.ValueNoise</c> 的运算顺序逐运算对应（角值由调用方按同式提前算好）。</para>
    /// </summary>
    public static void Bilinear(
        float c00, float c10, float c01, float c11,
        ReadOnlySpan<float> u, ReadOnlySpan<float> v, Span<float> dst)
    {
        if (u.Length != v.Length || u.Length != dst.Length)
            throw new ArgumentException("Bilinear 的 u/v/dst 长度必须一致");

        float d10 = c10 - c00;
        float d11 = c11 - c01;

        int w = Vector<float>.Count;
        int i = 0;

        var vc00 = new Vector<float>(c00);
        var vc01 = new Vector<float>(c01);
        var vd10 = new Vector<float>(d10);
        var vd11 = new Vector<float>(d11);

        for (; i + w <= u.Length; i += w)
        {
            var uu = new Vector<float>(u.Slice(i));
            var vv = new Vector<float>(v.Slice(i));
            var va = vc00 + vd10 * uu;
            var vb = vc01 + vd11 * uu;
            (va + (vb - va) * vv).CopyTo(dst.Slice(i));
        }
        for (; i < u.Length; i++)
        {
            float a = c00 + d10 * u[i];
            float b = c01 + d11 * u[i];
            dst[i] = a + (b - a) * v[i];
        }
    }

    /// <summary>
    /// 三线性（三维值噪声的体素复现），运算顺序与 <c>NoisePrimitives.ValueNoise3D</c> 逐运算对应：
    /// 先 x 向 4 条边、再 y 向 2 条，最后 z 向 1 条。
    /// </summary>
    public static void Trilinear(
        float c000, float c100, float c010, float c110,
        float c001, float c101, float c011, float c111,
        ReadOnlySpan<float> u, ReadOnlySpan<float> v, ReadOnlySpan<float> w, Span<float> dst)
    {
        if (u.Length != v.Length || u.Length != w.Length || u.Length != dst.Length)
            throw new ArgumentException("Trilinear 的 u/v/w/dst 长度必须一致");

        float d100 = c100 - c000;
        float d110 = c110 - c010;
        float d101 = c101 - c001;
        float d111 = c111 - c011;

        int width = Vector<float>.Count;
        int i = 0;

        var v000 = new Vector<float>(c000);
        var v010 = new Vector<float>(c010);
        var v001 = new Vector<float>(c001);
        var v011 = new Vector<float>(c011);
        var vd100 = new Vector<float>(d100);
        var vd110 = new Vector<float>(d110);
        var vd101 = new Vector<float>(d101);
        var vd111 = new Vector<float>(d111);

        for (; i + width <= u.Length; i += width)
        {
            var uu = new Vector<float>(u.Slice(i));
            var vv = new Vector<float>(v.Slice(i));
            var ww = new Vector<float>(w.Slice(i));

            var x00 = v000 + vd100 * uu;
            var x10 = v010 + vd110 * uu;
            var x01 = v001 + vd101 * uu;
            var x11 = v011 + vd111 * uu;

            var y0 = x00 + (x10 - x00) * vv;
            var y1 = x01 + (x11 - x01) * vv;

            (y0 + (y1 - y0) * ww).CopyTo(dst.Slice(i));
        }
        for (; i < u.Length; i++)
            dst[i] = TrilinearScalar(c000, c010, c001, c011, d100, d110, d101, d111, u[i], v[i], w[i]);
    }

    private static float TrilinearScalar(
        float c000, float c010, float c001, float c011,
        float d100, float d110, float d101, float d111,
        float u, float v, float w)
    {
        float x00 = c000 + d100 * u;
        float x10 = c010 + d110 * u;
        float x01 = c001 + d101 * u;
        float x11 = c011 + d111 * u;

        float y0 = x00 + (x10 - x00) * v;
        float y1 = x01 + (x11 - x01) * v;

        return y0 + (y1 - y0) * w;
    }

    /// <summary><c>|src| &lt; threshold</c> → 1，否则 0（无分支写入 byte 平面）。</summary>
    public static void MaskAbsLess(ReadOnlySpan<float> src, float threshold, Span<byte> dst)
    {
        if (dst.Length < src.Length)
            throw new ArgumentException("MaskAbsLess 的 dst 太短");

        var vt = new Vector<float>(threshold);
        int width = Vector<float>.Count;
        int i = 0;

        for (; i + width <= src.Length; i += width)
        {
            var x = new Vector<float>(src.Slice(i));
            var mask = Vector.LessThan(Vector.Abs(x), vt);
            PackMask(mask, dst, i, width);
        }
        for (; i < src.Length; i++)
            dst[i] = MathF.Abs(src[i]) < threshold ? (byte)1 : (byte)0;
    }

    /// <summary><c>src &gt;= threshold</c> → 1，否则 0。</summary>
    public static void ThresholdToByte(ReadOnlySpan<float> src, float threshold, Span<byte> dst)
    {
        if (dst.Length < src.Length)
            throw new ArgumentException("ThresholdToByte 的 dst 太短");

        var vt = new Vector<float>(threshold);
        int width = Vector<float>.Count;
        int i = 0;

        for (; i + width <= src.Length; i += width)
        {
            var x = new Vector<float>(src.Slice(i));
            var mask = Vector.GreaterThanOrEqual(x, vt);
            PackMask(mask, dst, i, width);
        }
        for (; i < src.Length; i++)
            dst[i] = src[i] >= threshold ? (byte)1 : (byte)0;
    }

    /// <summary>圆柱掩码：<c>dx² + dz² &lt;= radiusSq</c> → 1（不需要 sqrt）。</summary>
    public static void CylinderMask(ReadOnlySpan<float> dx, ReadOnlySpan<float> dz, float radiusSq, Span<byte> dst)
    {
        if (dx.Length != dz.Length || dst.Length < dx.Length)
            throw new ArgumentException("CylinderMask 的 dx/dz/dst 长度不匹配");

        var vr2 = new Vector<float>(radiusSq);
        int width = Vector<float>.Count;
        int i = 0;

        for (; i + width <= dx.Length; i += width)
        {
            var x = new Vector<float>(dx.Slice(i));
            var z = new Vector<float>(dz.Slice(i));
            var mask = Vector.LessThanOrEqual(x * x + z * z, vr2);
            PackMask(mask, dst, i, width);
        }
        for (; i < dx.Length; i++)
            dst[i] = dx[i] * dx[i] + dz[i] * dz[i] <= radiusSq ? (byte)1 : (byte)0;
    }

    /// <summary>椭球掩码：<c>dx² + dy² + dz² &lt;= radiusSq</c> → 1（不需要 sqrt）。</summary>
    public static void EllipsoidMask(
        ReadOnlySpan<float> dx, ReadOnlySpan<float> dy, ReadOnlySpan<float> dz,
        float radiusSq, Span<byte> dst)
    {
        if (dx.Length != dy.Length || dx.Length != dz.Length || dst.Length < dx.Length)
            throw new ArgumentException("EllipsoidMask 的 dx/dy/dz/dst 长度不匹配");

        var vr2 = new Vector<float>(radiusSq);
        int width = Vector<float>.Count;
        int i = 0;

        for (; i + width <= dx.Length; i += width)
        {
            var x = new Vector<float>(dx.Slice(i));
            var y = new Vector<float>(dy.Slice(i));
            var z = new Vector<float>(dz.Slice(i));
            var mask = Vector.LessThanOrEqual(x * x + y * y + z * z, vr2);
            PackMask(mask, dst, i, width);
        }
        for (; i < dx.Length; i++)
            dst[i] = dx[i] * dx[i] + dy[i] * dy[i] + dz[i] * dz[i] <= radiusSq ? (byte)1 : (byte)0;
    }

    /// <summary><c>dst = clamp(src + add, min, max)</c>。</summary>
    public static void AddClamp(ReadOnlySpan<float> src, float add, float min, float max, Span<float> dst)
    {
        if (dst.Length < src.Length)
            throw new ArgumentException("AddClamp 的 dst 太短");

        var va = new Vector<float>(add);
        var vmin = new Vector<float>(min);
        var vmax = new Vector<float>(max);

        int width = Vector<float>.Count;
        int i = 0;

        for (; i + width <= src.Length; i += width)
        {
            var x = new Vector<float>(src.Slice(i));
            Vector.Min(Vector.Max(x + va, vmin), vmax).CopyTo(dst.Slice(i));
        }
        for (; i < src.Length; i++)
        {
            float v = src[i] + add;
            dst[i] = v < min ? min : v > max ? max : v;
        }
    }

    private static void PackMask(Vector<int> mask, Span<byte> dst, int offset, int width)
    {
        for (int lane = 0; lane < width; lane++)
            dst[offset + lane] = mask[lane] != 0 ? (byte)1 : (byte)0;
    }
}
