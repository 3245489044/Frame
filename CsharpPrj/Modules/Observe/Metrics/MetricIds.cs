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

    /// <summary>当前帧流式提交区块数。</summary>
    ChunkStream = 5,
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
    public const int Count = 6;

    private static readonly string[] Names =
    {
        "collide.cellReads",
        "height.query",
        "sim.tick",
        "chunk.visible",
        "chunk.resident",
        "chunk.stream",
    };

    private static readonly MetricKind[] Kinds =
    {
        MetricKind.Counter,
        MetricKind.Histogram,
        MetricKind.Histogram,
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
