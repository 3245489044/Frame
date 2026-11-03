namespace MathematicalAlgorithms.Spatial;

/// <summary>
/// 分块脏块 / 版本追踪（生产侧）：写前 <see cref="BeginChange"/> 自增版本，
/// <see cref="Mark"/> 把区域覆盖的分块按键去重标脏并记录"首次触碰"的版本，<see cref="Drain"/> 取走并清空。
/// <para>与 <see cref="ResidencyScheduler"/>（消费侧调度）对偶；不认体素/节点/编辑语义，只对分块协议成立。</para>
/// <para>版本表只增不减（与既有内核一致）：已流式区块的签名依赖首次写入版本，不得顺手回收。</para>
/// </summary>
public sealed class ChunkDirtyTracker
{
    private readonly Dictionary<long, long> _versions = new();
    private readonly HashSet<long> _keys = new();
    private readonly List<DirtyChunk> _dirty = new();
    private long _version;

    public long Version => _version;
    public int PendingCount => _dirty.Count;

    /// <summary>分块最后一次写入时的版本；从未写过返回 0（签名兜底，不能返回 -1）。</summary>
    public long VersionOf(long chunkKey) => _versions.TryGetValue(chunkKey, out var v) ? v : 0L;

    /// <summary>变更前自增版本（写入/清空/读档前调用），返回新版本。</summary>
    public long BeginChange() => ++_version;

    /// <summary>把区域覆盖的分块标脏（按键去重，保持首次插入序）。</summary>
    public void Mark(in ChunkRegion region, int chunkSize)
    {
        foreach (var chunk in region.Chunks(chunkSize))
            Touch(chunk);
    }

    /// <summary>区块 + 外扩 1 格（面邻接影响的面相邻块）标脏。</summary>
    public void MarkExpanded(in ChunkCoord chunk, int chunkSize)
    {
        var bounds = chunk.Bounds(chunkSize);
        Mark(new ChunkRegion(
            bounds.MinX - 1, bounds.MinY - 1, bounds.MinZ - 1,
            bounds.MaxX + 1, bounds.MaxY + 1, bounds.MaxZ + 1), chunkSize);
    }

    /// <summary>取走当前脏块快照并清空；顺序 = 首次触碰撞序。</summary>
    public IReadOnlyList<DirtyChunk> Drain()
    {
        if (_dirty.Count == 0) return Array.Empty<DirtyChunk>();

        var drained = _dirty.ToArray();
        _dirty.Clear();
        _keys.Clear();
        return drained;
    }

    private void Touch(in ChunkCoord chunk)
    {
        _versions[chunk.Key] = _version;
        if (_keys.Add(chunk.Key))
            _dirty.Add(new DirtyChunk(chunk, _version));
    }
}
