using System.Runtime.InteropServices;

namespace Modules.Compute.Layout;

/// <summary>
/// 64B 对齐的原生缓冲（SIMD 平面起始对齐的唯一来源）。
/// <para>用 <see cref="NativeMemory.AlignedAlloc"/> 一次性分配；<see cref="As{T}"/> 只做视图转换，不做拷贝。</para>
/// <para>生命周期由持有者（<see cref="Arena"/> / <see cref="PaddedVolume{T}"/> / <see cref="SoaPlane{T}"/>）管理。</para>
/// </summary>
public sealed unsafe class AlignedBuffer : IDisposable
{
    private byte* _pointer;
    private readonly int _length;
    private readonly int _alignment;

    private AlignedBuffer(byte* pointer, int length, int alignment)
    {
        _pointer = pointer;
        _length = length;
        _alignment = alignment;
    }

    /// <param name="length">字节数（0 也合法：仍分配 1 字节以保证指针有效）。</param>
    /// <param name="alignment">2 的幂，默认 64（一个缓存行）。</param>
    public AlignedBuffer(int length, int alignment = 64)
    {
        if (length < 0)
            throw new ArgumentOutOfRangeException(nameof(length), "字节数不能为负");
        if (alignment <= 0 || (alignment & (alignment - 1)) != 0)
            throw new ArgumentOutOfRangeException(nameof(alignment), "对齐必须是 2 的幂");

        int bytes = Math.Max(length, 1);
        byte* pointer = (byte*)NativeMemory.AlignedAlloc((nuint)bytes, (nuint)alignment);
        if (pointer is null)
            throw new OutOfMemoryException($"AlignedAlloc 失败：{bytes} 字节（对齐 {alignment}）");

        _pointer = pointer;
        _length = length;
        _alignment = alignment;

        if (length > 0)
            Zero();
    }

    public int Length => _length;

    public int Alignment => _alignment;

    public bool IsEmpty => _length == 0;

    private void ThrowIfDisposed()
    {
        if (_pointer is null)
            throw new ObjectDisposedException(nameof(AlignedBuffer));
    }

    /// <summary>原始指针（仅用于租借缓冲区给原生/第三方接口）。</summary>
    public byte* Pointer
    {
        get
        {
            ThrowIfDisposed();
            return _pointer;
        }
    }

    public Span<byte> Bytes
    {
        get
        {
            ThrowIfDisposed();
            return new Span<byte>(_pointer, _length);
        }
    }

    /// <summary>按元素类型取视图；长度必须放得下。</summary>
    public Span<T> As<T>() where T : unmanaged
    {
        ThrowIfDisposed();
        int elementSize = sizeof(T);
        if (elementSize == 0)
            throw new InvalidOperationException($"类型 {typeof(T).Name} 的 sizeof 为 0");
        if (_length % elementSize != 0)
            throw new InvalidOperationException($"缓冲字节数 {_length} 不是 {typeof(T).Name} 元素大小 {elementSize} 的整数倍");

        return new Span<T>((T*)_pointer, _length / elementSize);
    }

    /// <summary>把整段清零（行尾补齐区必须是 0，避免脏数据参与比较）。</summary>
    public void Zero()
    {
        ThrowIfDisposed();
        if (_length > 0)
            NativeMemory.Clear(_pointer, (nuint)_length);
    }

    public void Dispose()
    {
        byte* pointer = _pointer;
        _pointer = null;
        if (pointer is not null)
            NativeMemory.AlignedFree(pointer);
    }
}
