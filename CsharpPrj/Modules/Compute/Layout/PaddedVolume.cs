using System.Runtime.CompilerServices;

namespace Modules.Compute.Layout;

/// <summary>
/// 带一圈 halo 的立方体（core 边长 + 两侧 margin），x 最快、64B 对齐、行尾不补齐（每条边整数格）。
/// <para>用于 padded 体素/掩码平面：中心区与 halo 走同一闭式索引，避免调用方再单独建数组。</para>
/// </summary>
public sealed class PaddedVolume<T> : IDisposable where T : unmanaged
{
    private readonly AlignedBuffer _buffer;
    private readonly int _core;
    private readonly int _margin;
    private readonly int _dim;

    /// <param name="core">中心区边长（&gt;0）。</param>
    /// <param name="margin">单侧 halo 厚度（≥0，通常 1）。</param>
    /// <param name="alignment">缓冲起始对齐（默认 64B）。</param>
    /// <param name="zeroPadding">true（默认）⇒ 构造后整段清零，halo 未显式写入处为 0。</param>
    public PaddedVolume(int core, int margin = 1, int alignment = 64, bool zeroPadding = true)
    {
        if (core <= 0)
            throw new ArgumentOutOfRangeException(nameof(core), "core 必须为正");
        if (margin < 0)
            throw new ArgumentOutOfRangeException(nameof(margin), "margin 不能为负");

        _core = core;
        _margin = margin;
        _dim = checked(core + margin * 2);
        _buffer = new AlignedBuffer(checked(_dim * _dim * _dim * Unsafe.SizeOf<T>()), alignment);

        if (zeroPadding)
            Clear();
    }

    /// <summary>含 halo 的边长（core + 2*margin）。</summary>
    public int Dim => _dim;

    /// <summary>中心区边长。</summary>
    public int Core => _core;

    /// <summary>单侧 halo 厚度。</summary>
    public int Margin => _margin;

    public int Length => checked(_dim * _dim * _dim);

    /// <summary>整卷视图（padded 坐标）。</summary>
    public Span<T> All => _buffer.As<T>();

    /// <summary>第 z 层平面（padded 坐标）。</summary>
    public Span<T> Plane(int z)
    {
        if ((uint)z >= (uint)_dim)
            throw new ArgumentOutOfRangeException(nameof(z));
        return All.Slice(z * _dim * _dim, _dim * _dim);
    }

    /// <summary>padded 坐标下一段连续行（供 SIMD 直接吃）。</summary>
    public Span<T> Row(int y, int z, int x0, int len)
    {
        if ((uint)y >= (uint)_dim || (uint)z >= (uint)_dim)
            throw new ArgumentOutOfRangeException(nameof(y));
        if (x0 < 0 || len < 0 || x0 + len > _dim)
            throw new ArgumentOutOfRangeException(nameof(len));

        int start = x0 + _dim * (y + _dim * z);
        return All.Slice(start, len);
    }

    /// <summary>中心区的一行（跳过左右 halo）。</summary>
    public Span<T> CoreRow(int y, int z)
    {
        if ((uint)y >= (uint)_dim || (uint)z >= (uint)_dim)
            throw new ArgumentOutOfRangeException(nameof(y));
        int start = _margin + _dim * (y + _dim * z);
        return All.Slice(start, _core);
    }

    /// <summary>padded 坐标索引（不做边界检查，热路径用）。</summary>
    public T this[int x, int y, int z]
    {
        get => All[x + _dim * (y + _dim * z)];
        set => All[x + _dim * (y + _dim * z)] = value;
    }

    /// <summary>中心区坐标（自动加 margin）。</summary>
    public T CoreValue(int x, int y, int z)
    {
        if (!IsCore(x) || !IsCore(y) || !IsCore(z))
            throw new ArgumentOutOfRangeException(nameof(x));

        return All[(x + _margin) + _dim * ((y + _margin) + _dim * (z + _margin))];
    }

    private bool IsCore(int value) => (uint)value < (uint)_core;

    public void Clear() => _buffer.Zero();

    public void Dispose() => _buffer.Dispose();
}
