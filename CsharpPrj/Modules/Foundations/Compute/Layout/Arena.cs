using System.Runtime.InteropServices;

namespace Modules.Compute.Layout;

/// <summary>
/// 每 worker 一条的线性（bump）分配器：padded 缓冲、平面掩码、光照队列全部从这里借，生命周期 ≤ 1 chunk。
/// <para>作用域归还用 <see cref="Scope"/>；作用域外只允许 <see cref="Reset"/>（整条回退）。</para>
/// <para><b>线程约定</b>：实例绑定创建线程；跨线程使用直接抛，避免静默数据竞争。</para>
/// </summary>
public sealed unsafe class Arena : IDisposable
{
    private readonly AlignedBuffer _buffer;
    private readonly int _ownerThreadId;

    private long _offset;
    private bool _disposed;

    /// <param name="capacityBytes">容量（默认 8MB；按 worker 配置）。</param>
    /// <param name="alignment">缓冲起始对齐（默认 64B）。</param>
    public Arena(int capacityBytes = 8 * 1024 * 1024, int alignment = 64)
    {
        if (capacityBytes <= 0)
            throw new ArgumentOutOfRangeException(nameof(capacityBytes), "Arena 容量必须为正");

        _buffer = new AlignedBuffer(capacityBytes, alignment);
        _ownerThreadId = Environment.CurrentManagedThreadId;
    }

    public int Capacity => _buffer.Length;

    public long Used => _offset;

    public long Remaining => _buffer.Length - _offset;

    public int Alignment => _buffer.Alignment;

    /// <summary>进入作用域：<see cref="ArenaScope.Dispose"/> 时回退到当前 offset。</summary>
    public ArenaScope Scope()
    {
        EnsureUsable();
        return new ArenaScope(this, _offset);
    }

    /// <summary>整条回退（chunk/tile 结束时调用）。</summary>
    public void Reset()
    {
        EnsureUsable();
        _offset = 0;
    }

    /// <summary>按元素类型分配一段（起始按 <paramref name="align"/> 对齐）。</summary>
    public Span<T> Alloc<T>(int count, int align = 64) where T : unmanaged
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        if (align <= 0 || (align & (align - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(align), "对齐必须是 2 的幂");

        EnsureUsable();

        long bytes = checked((long)count * sizeof(T));
        long start = AlignUp(_offset, align);
        long end = start + bytes;
        if (end > _buffer.Length)
            throw new InvalidOperationException(
                $"Arena 容量不足：需要 {bytes}B（起点 {start}），容量 {_buffer.Length}B，已用 {_offset}B");

        _offset = end;
        return new Span<T>((T*)(_buffer.Pointer + start), count);
    }

    public Span<byte> AllocBytes(int count, int align = 64) => Alloc<byte>(count, align);

    /// <summary>只推进指针、不做类型转换（用于自定义头部布局）。</summary>
    public long Reserve(int bytes, int align = 64)
    {
        if (bytes < 0)
            throw new ArgumentOutOfRangeException(nameof(bytes));
        if (align <= 0 || (align & (align - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(align), "对齐必须是 2 的幂");

        EnsureUsable();

        long start = AlignUp(_offset, align);
        long end = start + bytes;
        if (end > _buffer.Length)
            throw new InvalidOperationException($"Arena 容量不足：需要 {bytes}B（起点 {start}），容量 {_buffer.Length}B");

        _offset = end;
        return start;
    }

    internal void Rollback(long mark)
    {
        if (mark < 0 || mark > _buffer.Length)
            throw new ArgumentOutOfRangeException(nameof(mark));
        if (mark < _offset)
            _offset = mark;
    }

    private static long AlignUp(long value, int alignment) =>
        (value + alignment - 1) & ~(long)(alignment - 1);

    private void EnsureUsable()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(Arena));
        if (Environment.CurrentManagedThreadId != _ownerThreadId)
            throw new InvalidOperationException("Arena 绑定创建线程；请按 worker 各建一条");
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        _buffer.Dispose();
    }
}
