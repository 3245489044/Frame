using System;
using System.Numerics;

namespace MathematicalAlgorithms.Spatial;

/// <summary>
/// 分块列占用摘要：每个分块 32×32 列，每列一个 <see cref="uint"/> 位掩码（第 i 位 = 本地 Y=i 实心）。
/// <para>由既有分块载荷（<see cref="ChunkPayloadCache{T}"/>）派生，签名命中即复用，不额外读取世界。</para>
/// <para>纯机械层：只表达"哪些格实心"，不认节点身份、液体策略、重力或输入语义。</para>
/// </summary>
public sealed class ChunkOccupancy
{
    public readonly int Size;
    public readonly int BaseX;
    public readonly int BaseY;
    public readonly int BaseZ;

    /// <summary>列掩码，索引 = lz * Size + lx。</summary>
    public readonly uint[] Masks;

    /// <summary>列最低实心世界 Y；空列 = int.MaxValue。</summary>
    public readonly int[] MinY;

    /// <summary>列最高实心世界 Y；空列 = int.MinValue。</summary>
    public readonly int[] MaxY;

    internal ChunkOccupancy(int size, in ChunkCoord chunk)
    {
        Size = size;
        BaseX = chunk.X * size;
        BaseY = chunk.Y * size;
        BaseZ = chunk.Z * size;
        Masks = new uint[size * size];
        MinY = new int[size * size];
        MaxY = new int[size * size];
    }
}

/// <summary>
/// 列占用摘要来源。<see cref="Get"/> 返回 <c>null</c> 表示该分块不存在/在世界外（按空气处理）。
/// <para>实现方负责签名缓存；调用方（扫掠）只在移动前导面切换时取一次。</para>
/// </summary>
public interface IOccupancySource
{
    int ChunkSize { get; }

    ChunkOccupancy? Get(in ChunkCoord chunk);
}

/// <summary>
/// 从分块载荷缓存派生列占用摘要：与网格化/碰撞共用同一份载荷，签名变化才重建。
/// <para><paramref name="inBounds"/> 为 null 时视为全部在界内；返回 false 的分块不填充、按空气处理。</para>
/// </summary>
public sealed class PayloadOccupancySource<T> : IOccupancySource
{
    private sealed class Entry
    {
        public ChunkOccupancy Occupancy = null!;
        public long Signature;
    }

    private readonly ChunkPayloadCache<T> _payloads;
    private readonly Func<T, bool> _isSolid;
    private readonly Func<ChunkCoord, long> _signatureOf;
    private readonly Action<ChunkCoord, T[]> _fill;
    private readonly Func<ChunkCoord, bool>? _inBounds;
    private readonly int _size;
    private readonly int _shift;
    private readonly Dictionary<long, Entry> _entries = new();

    public PayloadOccupancySource(
        ChunkPayloadCache<T> payloads,
        Func<T, bool> isSolid,
        Func<ChunkCoord, long> signatureOf,
        Action<ChunkCoord, T[]> fill,
        Func<ChunkCoord, bool>? inBounds = null)
    {
        _payloads = payloads ?? throw new ArgumentNullException(nameof(payloads));
        _isSolid = isSolid ?? throw new ArgumentNullException(nameof(isSolid));
        _signatureOf = signatureOf ?? throw new ArgumentNullException(nameof(signatureOf));
        _fill = fill ?? throw new ArgumentNullException(nameof(fill));
        _inBounds = inBounds;
        _size = payloads.ChunkSize;
        _shift = ChunkSpace.ShiftOf(_size);
    }

    public int ChunkSize => _size;

    public int ResidentCount => _entries.Count;

    public ChunkOccupancy? Get(in ChunkCoord chunk)
    {
        if (_inBounds is not null && !_inBounds(chunk)) return null;

        long key = chunk.Key;
        long signature = _signatureOf(chunk);
        if (_entries.TryGetValue(key, out var entry) && entry.Signature == signature)
            return entry.Occupancy;

        T[] payload = _payloads.TryGet(chunk, signature)
            ?? _payloads.GetOrBuild(chunk, signature, _fill, out _);

        var occupancy = Build(chunk, payload);
        if (entry is null)
            _entries[key] = new Entry { Occupancy = occupancy, Signature = signature };
        else
        {
            entry.Occupancy = occupancy;
            entry.Signature = signature;
        }
        return occupancy;
    }

    public void Invalidate(in ChunkCoord chunk) => _entries.Remove(chunk.Key);
    public void Remove(in ChunkCoord chunk) => _entries.Remove(chunk.Key);
    public void Clear() => _entries.Clear();

    private ChunkOccupancy Build(in ChunkCoord chunk, T[] payload)
    {
        int size = _size;
        var occupancy = new ChunkOccupancy(size, chunk);
        uint[] masks = occupancy.Masks;
        int[] minY = occupancy.MinY;
        int[] maxY = occupancy.MaxY;
        int colZShift = _shift * 2;

        for (int lz = 0; lz < size; lz++)
        {
            int zBase = lz << colZShift;
            int colRow = lz * size;
            for (int lx = 0; lx < size; lx++)
            {
                int slot = lx | zBase;
                uint mask = 0;
                for (int ly = 0; ly < size; ly++)
                {
                    if (_isSolid(payload[slot | (ly << _shift)]))
                        mask |= 1u << ly;
                }

                masks[colRow + lx] = mask;
                if (mask == 0)
                {
                    minY[colRow + lx] = int.MaxValue;
                    maxY[colRow + lx] = int.MinValue;
                }
                else
                {
                    minY[colRow + lx] = occupancy.BaseY + BitOperations.TrailingZeroCount(mask);
                    maxY[colRow + lx] = occupancy.BaseY + 31 - BitOperations.LeadingZeroCount(mask);
                }
            }
        }

        return occupancy;
    }
}
