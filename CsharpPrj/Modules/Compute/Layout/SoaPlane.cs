using System.Runtime.CompilerServices;

namespace Modules.Compute.Layout;

/// <summary>
/// SoA 平面：一条通道一条平面（<c>opaque</c>/<c>emission</c>/<c>light</c>/<c>cullClass</c> 各自 byte，<c>height</c> 为 float/int）。
/// <para>行长按 64B 的整数倍补齐，保证每条行起点对齐、SIMD 加载无跨通道污染。</para>
/// <para>行尾补齐区必须保持为 0；<see cref="ClearRowTail"/> 用于写入后清理。</para>
/// </summary>
public sealed class SoaPlane<T> : IDisposable where T : unmanaged
{
    private readonly AlignedBuffer _buffer;
    private readonly int _nx;
    private readonly int _ny;
    private readonly int _stride;

    public SoaPlane(int nx, int ny, int alignment = 64)
    {
        if (nx <= 0 || ny <= 0)
            throw new ArgumentOutOfRangeException(nameof(nx), "平面边长必须为正");
        if (alignment <= 0 || (alignment & (alignment - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(alignment), "对齐必须是 2 的幂");

        int element = Unsafe.SizeOf<T>();
        if (element <= 0)
            throw new InvalidOperationException($"类型 {typeof(T).Name} 的 sizeof 为 0");

        // 行长（元素数）必须让 rowBytes 为 alignment 的整数倍
        int unit = alignment / Gcd(alignment, element);
        _nx = nx;
        _ny = ny;
        _stride = AlignUp(nx, unit);
        _buffer = new AlignedBuffer(checked(_stride * ny * element), alignment);

        Clear();
    }

    public int Nx => _nx;

    public int Ny => _ny;

    /// <summary>行长（元素数，含 64B 补齐）。</summary>
    public int Stride => _stride;

    /// <summary>行长（字节数，恒为 alignment 的整数倍）。</summary>
    public int RowBytes => _stride * Unsafe.SizeOf<T>();

    public Span<T> All => _buffer.As<T>();

    /// <summary>整行（含补齐区）。</summary>
    public Span<T> Row(int y)
    {
        if ((uint)y >= (uint)_ny)
            throw new ArgumentOutOfRangeException(nameof(y));
        return All.Slice(y * _stride, _stride);
    }

    /// <summary>行内一段（可见数据区，不含补齐）。</summary>
    public Span<T> Row(int y, int x0, int len)
    {
        if ((uint)y >= (uint)_ny)
            throw new ArgumentOutOfRangeException(nameof(y));
        if (x0 < 0 || len < 0 || x0 + len > _nx)
            throw new ArgumentOutOfRangeException(nameof(len));
        return All.Slice(y * _stride + x0, len);
    }

    public T this[int x, int y]
    {
        get => All[y * _stride + x];
        set => All[y * _stride + x] = value;
    }

    /// <summary>清掉第 y 行的补齐区（可见区已写、补齐区必须为 0）。</summary>
    public void ClearRowTail(int y)
    {
        if ((uint)y >= (uint)_ny)
            throw new ArgumentOutOfRangeException(nameof(y));
        int start = y * _stride + _nx;
        All.Slice(start, _stride - _nx).Clear();
    }

    public void Clear() => _buffer.Zero();

    public void Dispose() => _buffer.Dispose();

    private static int AlignUp(int value, int alignment) => (value + alignment - 1) & ~(alignment - 1);

    private static int Gcd(int a, int b)
    {
        while (b != 0)
        {
            int t = a % b;
            a = b;
            b = t;
        }
        return Math.Abs(a);
    }
}
