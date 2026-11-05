using System.Runtime.CompilerServices;

namespace Modules.Compute.Layout;

/// <summary>
/// 跨 chunk 复用的类型化缓冲池（按 <c>(Type, count)</c> 分桶）。
/// <para>池化上限按内存预算配置；超过上限一律丢弃，不做无界增长。</para>
/// <para>租到的对象是托管数组，天然可用于跨线程输出（顶点列表等）。</para>
/// </summary>
public sealed class ScratchPool : IDisposable
{
    private sealed class Bucket
    {
        public readonly Stack<Array> Free = new();
        public int ByteSize;
    }

    private readonly Dictionary<(Type, int), Bucket> _buckets = new();
    private readonly int _maxRetainedBytes;
    private int _retainedBytes;
    private bool _disposed;

    /// <param name="maxRetainedBytes">池内保留上限（默认 4MB）。</param>
    public ScratchPool(int maxRetainedBytes = 4 * 1024 * 1024)
    {
        if (maxRetainedBytes < 0)
            throw new ArgumentOutOfRangeException(nameof(maxRetainedBytes));
        _maxRetainedBytes = maxRetainedBytes;
    }

    public int RetainedBytes => _retainedBytes;

    /// <summary>租一段；<see cref="ScratchBuffer{T}.Dispose"/> 归还。</summary>
    public ScratchBuffer<T> Rent<T>(int count) where T : unmanaged
    {
        if (count < 0)
            throw new ArgumentOutOfRangeException(nameof(count));
        ThrowIfDisposed();

        var key = (typeof(T), count);
        if (_buckets.TryGetValue(key, out var bucket) && bucket.Free.Count > 0)
        {
            T[] cached = (T[])bucket.Free.Pop();
            _retainedBytes -= bucket.ByteSize;
            return new ScratchBuffer<T>(this, cached, count, bucket.ByteSize);
        }

        int bytes = checked(count * Unsafe.SizeOf<T>());
        return new ScratchBuffer<T>(this, new T[count], count, bytes);
    }

    internal void Return(Array array, int byteSize)
    {
        if (_disposed || byteSize <= 0)
            return;

        int count = array.Length;
        var key = (array.GetType().GetElementType()!, count);

        if (_retainedBytes + byteSize > _maxRetainedBytes)
            return;

        if (!_buckets.TryGetValue(key, out var bucket))
        {
            bucket = new Bucket { ByteSize = byteSize };
            _buckets[key] = bucket;
        }

        bucket.Free.Push(array);
        _retainedBytes += byteSize;
    }

    /// <summary>清空池（内存紧张/签名变更时调用）。</summary>
    public void Trim()
    {
        _buckets.Clear();
        _retainedBytes = 0;
    }

    private void ThrowIfDisposed()
    {
        if (_disposed)
            throw new ObjectDisposedException(nameof(ScratchPool));
    }

    public void Dispose()
    {
        if (_disposed)
            return;
        _disposed = true;
        Trim();
    }
}

/// <summary>一次 Scratch 租约：Dispose 归还；重复 Dispose 无副作用。</summary>
public struct ScratchBuffer<T> : IDisposable where T : unmanaged
{
    private readonly ScratchPool? _pool;
    private readonly T[] _array;
    private readonly int _length;
    private readonly int _byteSize;
    private bool _returned;

    internal ScratchBuffer(ScratchPool pool, T[] array, int length, int byteSize)
    {
        _pool = pool;
        _array = array;
        _length = length;
        _byteSize = byteSize;
        _returned = false;
    }

    public int Length => _length;

    public Span<T> Span => new(_array, 0, _length);

    public void Dispose()
    {
        if (_returned)
            return;
        _returned = true;
        _pool?.Return(_array, _byteSize);
    }
}
