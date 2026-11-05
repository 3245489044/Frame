namespace Modules.Observe.Metrics;

/// <summary>
/// 指标身份：枚举固定顺序，下标即预分配数组索引。禁止运行时拼接字符串，名字只在输出时查一次表。
/// </summary>
public enum MetricId
{
    /// <summary>碰撞查询读到的列占位数（solid() 语义）。</summary>
    CollideCellReads = 0,

    /// <summary>列高区间求值耗时（<c>TerrainHeightTiles.Query</c>）。</summary>
    HeightQuery = 1,

    /// <summary>角色仿真单帧推进耗时。</summary>
    SimTick = 2,

    /// <summary>当前帧可见区块数。</summary>
    ChunkVisible = 3,

    /// <summary>当前驻留（已流式）区块数。</summary>
    ChunkResident = 4,

    /// <summary>当前帧 ECS 区块实体数（可见集合投影，不等于“本帧提交数”）。</summary>
    ChunkEntities = 5,

    /// <summary>真正发生构建的网格耗时（缓存命中不计入；命中记在 <see cref="MeshLookup"/>）。</summary>
    MeshBuild = 6,

    /// <summary>网格预算用尽导致本帧提前收手的次数。</summary>
    MeshBudgetHit = 7,

    /// <summary>分块负载缓存命中次数。</summary>
    ChunkCacheHit = 8,

    /// <summary>分块负载缓存未命中次数。</summary>
    ChunkCacheMiss = 9,

    /// <summary>帧阶段：输入解析与视口尺寸。</summary>
    FrameInput = 10,

    /// <summary>帧阶段：可见性 + 网格流式提交（<c>ChunkMeshStreamer.Update</c>）。</summary>
    FrameStream = 11,

    /// <summary>帧阶段：ECS chunk 投影（<c>SyncVoxelChunks</c>）。</summary>
    FrameSync = 12,

    /// <summary>帧阶段：ECS 系统更新。</summary>
    FrameSystems = 13,

    /// <summary>帧阶段：帧级状态写入。</summary>
    FrameState = 14,

    /// <summary>帧阶段：Mesh 上屏渲染。</summary>
    FrameRender = 15,

    /// <summary>网格构建函数的缓存命中次数（未真正构建）。</summary>
    MeshLookup = 16,

    /// <summary>当前帧候选分块数（候选域枚举结果；与可见集合同一次采集）。</summary>
    ChunkCandidate = 17,

    /// <summary>当前帧几何通过（视锥 + 距离，配额截断前）的分块数。</summary>
    ChunkAdmitted = 18,

    /// <summary>当前已上传网格的分块数（“入网格”集合；不等于驻留集合）。</summary>
    ChunkMeshed = 19,
}

/// <summary>指标类型：决定写入 cell 的方式（Counter 累加 / Gauge 水位置 / Histogram 分位）。</summary>
public enum MetricKind
{
    Counter,
    Gauge,
    Histogram,
}

/// <summary>
/// 指标元数据：与 <see cref="MetricId"/> 一一对应的稳定名字与类型表，只在 flush 时读取。
/// </summary>
public static class MetricCatalog
{
    public const int Count = 20;

    private static readonly string[] Names =
    {
        "collide.cellReads",
        "height.query",
        "sim.tick",
        "chunk.visible",
        "chunk.resident",
        "chunk.entities",
        "mesh.build",
        "mesh.budgetHit",
        "chunk.cacheHit",
        "chunk.cacheMiss",
        "frame.input",
        "frame.stream",
        "frame.sync",
        "frame.systems",
        "frame.state",
        "frame.render",
        "mesh.lookup",
        "chunk.candidate",
        "chunk.admitted",
        "chunk.meshed",
    };

    private static readonly MetricKind[] Kinds =
    {
        MetricKind.Counter,
        MetricKind.Histogram,
        MetricKind.Histogram,
        MetricKind.Gauge,
        MetricKind.Gauge,
        MetricKind.Gauge,
        MetricKind.Histogram,
        MetricKind.Counter,
        MetricKind.Counter,
        MetricKind.Counter,
        MetricKind.Histogram,
        MetricKind.Histogram,
        MetricKind.Histogram,
        MetricKind.Histogram,
        MetricKind.Histogram,
        MetricKind.Histogram,
        MetricKind.Counter,
        MetricKind.Gauge,
        MetricKind.Gauge,
        MetricKind.Gauge,
    };

    public static string NameOf(MetricId id)
    {
        int index = (int)id;
        return (uint)index < (uint)Names.Length ? Names[index] : id.ToString();
    }

    public static MetricKind KindOf(MetricId id)
    {
        int index = (int)id;
        return (uint)index < (uint)Kinds.Length ? Kinds[index] : MetricKind.Counter;
    }
}
