using MathematicalAlgorithms.Core;
using MathematicalAlgorithms.Spatial;
using Modules.Compute.Adapters;

namespace MathematicalAlgorithms.Noise;

/// <summary>地形高度带（纯内容语义，不含节点 ID；节点映射由上层声明面决定）。</summary>
public enum TerrainBand
{
    Air,
    Water,
    Sand,
    Grass,
    Dirt,
    Stone
}

/// <summary>
/// 地形常量（cm 世界）：声明面米制值 + 单位换算的唯一结果。
/// <para>CPU 生成器与 GPU 常量注入都从这里取值；改一处即两侧同步。</para>
/// </summary>
public readonly record struct TerrainConstants(
    int UnitsPerMeter,
    int SeaLevelCm,
    int SoilDepthCm,
    float HeightScaleCm,
    float HeightOffsetCm,
    int SandBandCm,
    int NoiseCell16,
    int NoiseCell8,
    int NoiseCell4)
{
    public static TerrainConstants From(in TerrainProfile profile, int unitsPerMeter)
    {
        if (unitsPerMeter <= 0)
            throw new ArgumentOutOfRangeException(nameof(unitsPerMeter), "单位/米必须为正");

        return new TerrainConstants(
            UnitsPerMeter: unitsPerMeter,
            SeaLevelCm: profile.SeaLevel * unitsPerMeter,
            SoilDepthCm: profile.SoilDepth * unitsPerMeter,
            HeightScaleCm: profile.HeightScale * unitsPerMeter,
            HeightOffsetCm: profile.HeightOffset * unitsPerMeter,
            SandBandCm: unitsPerMeter,
            NoiseCell16: 16 * unitsPerMeter,
            NoiseCell8: 8 * unitsPerMeter,
            NoiseCell4: 4 * unitsPerMeter);
    }
}

/// <summary>
/// 地形高度公式 <c>H(x,z) → groundY → band(x,y,z)</summary> 的唯一实现（纯函数、无状态变化）。
/// <ul>
///   <li>只依赖 x,z 的高度 H 可在列/瓦片层缓存：同一列的所有 y 复用一次噪声。</li>
///   <li>高度仍是「基线高度噪声 × 群系幅度 + 群系偏移」，没有第二套高度公式；
///       未注入 <see cref="BiomeField"/> 时逐字退化为原公式。</li>
///   <li><see cref="Band"/> 的常量与判据顺序仍与 HLSL <c>GetNode</c> 共用（群系调制只作用于 CPU 世界生成的列高）。</li>
/// </ul>
/// </summary>
public sealed class TerrainHeightModel
{
    private readonly int _seed;
    private readonly TerrainConstants _constants;
    private readonly BiomeField? _biomes;
    private readonly int _surfaceExtraAboveCm;
    private readonly long _baseSignature;
    private IHeightSpanSource? _contentSpans;
    private TerrainHeightBatchEvaluator? _batch;

    /// <summary>
    /// <paramref name="biomes"/> 为 null ⇒ 保持原「单调高度」公式；非 null ⇒ 高度幅度/偏移由群系决定。
    /// <paramref name="surfaceExtraAboveCm"/> 把地表以上的可见内容（树冠）并入 <see cref="SurfaceSpan"/>。
    /// </summary>
    public TerrainHeightModel(
        in TerrainProfile profile,
        int seed,
        int unitsPerMeter = 100,
        BiomeField? biomes = null,
        int surfaceExtraAboveCm = 0)
    {
        _seed = seed;
        _constants = TerrainConstants.From(profile, unitsPerMeter);
        _biomes = biomes;
        _surfaceExtraAboveCm = Math.Max(0, surfaceExtraAboveCm);

        var hash = SignatureHash.Begin();
        hash = SignatureHash.Mix(hash, seed);
        hash = SignatureHash.Mix(hash, _constants.UnitsPerMeter);
        hash = SignatureHash.Mix(hash, profile.SeaLevel);
        hash = SignatureHash.Mix(hash, profile.SoilDepth);
        hash = SignatureHash.Mix(hash, profile.HeightScale);
        hash = SignatureHash.Mix(hash, profile.HeightOffset);
        hash = SignatureHash.Mix(hash, _surfaceExtraAboveCm);
        if (biomes is not null)
            hash = SignatureHash.Mix(hash, biomes.Signature);

        _baseSignature = hash;
        Signature = hash;
    }

    public TerrainConstants Constants => _constants;

    public int Seed => _seed;

    public int UnitsPerMeter => _constants.UnitsPerMeter;

    /// <summary>是否接入群系场（未接入时 <see cref="SampleBiome"/> 返回 default）。</summary>
    public bool HasBiomes => _biomes is not null;

    /// <summary>群系数量（未接入为 0）。</summary>
    public int BiomeCount => _biomes?.Biomes.Count ?? 0;

    /// <summary>群系列表（未接入为 null）；只读，供批量求值/签名使用。</summary>
    public IReadOnlyList<BiomeClimate>? Biomes => _biomes?.Biomes;

    /// <summary>
    /// 高度批量求值器（通用计算基底路径）；旧 <see cref="HeightCm"/> 原样保留为逐位基准。
    /// <para>实例无可变 scratch（<c>TileCache</c> 并发安全）⇒ 可跨 worker 共用。</para>
    /// </summary>
    public TerrainHeightBatchEvaluator Batch => _batch ??= new TerrainHeightBatchEvaluator(this);

    /// <summary>批量高度（cm，未取整）：逐点结果与 <see cref="HeightCm"/> 逐位一致（A 档）。</summary>
    public void FillHeightCm(GridRect rect, Span<float> dst) => Batch.FillHeightCm(rect, dst);

    /// <summary>批量地表 Y（cm）：逐点结果与 <see cref="GroundYCm"/> 逐位一致（A 档）。</summary>
    public void FillGroundYCm(GridRect rect, Span<int> dst) => Batch.FillGroundYCm(rect, dst);

    /// <summary>在 (x,z) 处采样群系（世界坐标；未接入群系时返回 default）。</summary>
    public BiomeSample SampleBiome(int x, int z) => _biomes?.Sample(x, z) ?? default;

    /// <summary>
    /// 公式签名：seed + 全部地形常量 + 单位 + 群系场 + 内容范围源。
    /// <para>注入 <see cref="IHeightSpanSource"/> 时会重算（<see cref="TerrainHeightTiles"/> 据此自动失效）。</para>
    /// </summary>
    public long Signature { get; private set; }

    /// <summary>可见内容垂直范围的数据源（分节流水线）；未注入为 null。</summary>
    public IHeightSpanSource? ContentSpans => _contentSpans;

    /// <summary>
    /// 注入内容范围源（只允许一次且实例唯一）：不同实例 ⇒ 抛，避免同一模型出现两套可见范围。
    /// <para>注入后重算 <see cref="Signature"/>，L1 瓦片缓存按签名失效。</para>
    /// </summary>
    public void SetContentSpans(IHeightSpanSource spans)
    {
        ArgumentNullException.ThrowIfNull(spans);
        if (_contentSpans is not null)
        {
            if (ReferenceEquals(_contentSpans, spans))
                return;
            throw new InvalidOperationException("同一地形模型不允许注入第二个内容范围源");
        }

        _contentSpans = spans;
        Signature = SignatureHash.Mix(_baseSignature, spans.Signature);
    }

    /// <summary>
    /// 高度（cm，未取整）：基线高度噪声 × 群系幅度 + 群系偏移。
    /// <para>未注入群系时与 GPU <c>HeightOffset + ComputeHeight(x,z) * HeightScale</c> 同式同序。</para>
    /// </summary>
    public float HeightCm(int x, int z)
    {
        float noise = TerrainNoise.ComputeHeight(x, z, _seed, _constants.UnitsPerMeter);
        if (_biomes is null)
            return _constants.HeightOffsetCm + noise * _constants.HeightScaleCm;

        var sample = _biomes.Sample(x, z);
        return HeightFormula.Evaluate(_constants, _biomes.Biomes, sample, noise);
    }

    /// <summary>地表 Y（cm，四舍六入五取偶，与 GPU RoundEven 对齐）。</summary>
    public int GroundYCm(int x, int z) => (int)MathF.Round(HeightCm(x, z));

    /// <summary>单格高度带判定：判据顺序与迁移前 <c>VoxelTerrainGen.Generate</c> 完全一致。</summary>
    public TerrainBand Band(int x, int y, int z) => Band(y, GroundYCm(x, z));

    /// <summary>已算出列高时的分带判定（列高缓存路径：每列只算一次噪声）。</summary>
    public TerrainBand Band(int y, int groundYCm)
    {
        int ground = groundYCm;

        if (y > ground)
            return y <= _constants.SeaLevelCm ? TerrainBand.Water : TerrainBand.Air;

        if (y == ground)
            return ground <= _constants.SeaLevelCm + _constants.SandBandCm
                ? TerrainBand.Sand
                : TerrainBand.Grass;

        if (y >= ground - _constants.SoilDepthCm)
            return TerrainBand.Dirt;

        return TerrainBand.Stone;
    }

    /// <summary>
    /// 水平 footprint 内的地表 Y 采样区间（保守）：按 1/8 m 步长在网格点上求值，包含四角与上界。
    /// <para>用于高度加速提示；步长小于最小的 4 m 噪声波长，外接查询时再由 <see cref="SurfaceSpan"/> 加余量。</para>
    /// </summary>
    public HeightSpan GroundSpan(int minX, int minZ, int maxX, int maxZ)
    {
        int x0 = Math.Min(minX, maxX), x1 = Math.Max(minX, maxX);
        int z0 = Math.Min(minZ, maxZ), z1 = Math.Max(minZ, maxZ);
        int step = Math.Max(1, _constants.UnitsPerMeter / 8);

        int minGround = int.MaxValue, maxGround = int.MinValue;
        for (int z = z0; ; z += step)
        {
            int sz = Math.Min(z, z1);
            for (int x = x0; ; x += step)
            {
                int sx = Math.Min(x, x1);
                int ground = GroundYCm(sx, sz);
                if (ground < minGround) minGround = ground;
                if (ground > maxGround) maxGround = ground;
                if (sx >= x1) break;
            }
            if (sz >= z1) break;
        }

        // 补中心点：chunk footprint 的查询点（自检与网格中心）可能落在粗采样格之间。
        if (minGround != int.MaxValue)
        {
            int centerGround = GroundYCm((x0 + x1) / 2, (z0 + z1) / 2);
            if (centerGround < minGround) minGround = centerGround;
            if (centerGround > maxGround) maxGround = centerGround;
        }

        return minGround == int.MaxValue ? HeightSpan.Empty : new HeightSpan(minGround, maxGround);
    }

    /// <summary>
    /// 水平 footprint 内「可能产生可见面」的高度带：地表 ~ 地表向下 SoilDepth（土层），
    /// 且当 footprint 内存在低于海平面的列时，上界抬到海平面（水面是有可见顶面的液体块）。
    /// <para>再并集注入的 <see cref="ContentSpans"/>（洞穴/矿脉向下的范围、植被向上的范围），
    /// 由各 section 显式声明，不再靠固定常量猜测。</para>
    /// <paramref name="marginCm"/> 覆盖采样步长内的插值起伏；逐 chunk 查询时传 0，避免把上方空气块带进候选。
    /// </summary>
    public HeightSpan SurfaceSpan(int minX, int minZ, int maxX, int maxZ, int marginCm = 0)
    {
        var ground = GroundSpan(minX, minZ, maxX, maxZ);
        if (ground.IsEmpty) return HeightSpan.Empty;

        int minY = ground.MinY - _constants.SoilDepthCm;
        int maxY = ground.MaxY;
        // 水体带：ground < SeaLevel 的列会在 (ground, SeaLevel] 生成 Water，其顶面必须进入候选。
        if (ground.MinY < _constants.SeaLevelCm)
            maxY = Math.Max(maxY, _constants.SeaLevelCm);
        // 地表以上还有树冠等可见内容（由世界生成器注入的保守上界）。
        maxY += _surfaceExtraAboveCm;

        // 内容范围：由分节流水线按真实 section 声明求值（洞穴/矿脉向下，植被向上）。
        if (_contentSpans is not null)
        {
            var content = _contentSpans.HeightSpanFor(ground.MinY, ground.MaxY, _constants.SeaLevelCm);
            if (!content.IsEmpty)
            {
                minY = Math.Min(minY, content.MinY);
                maxY = Math.Max(maxY, content.MaxY);
            }
        }

        return marginCm > 0 ? new HeightSpan(minY, maxY).Expand(marginCm) : new HeightSpan(minY, maxY);
    }
}
