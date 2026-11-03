using MathematicalAlgorithms.Core;
using MathematicalAlgorithms.Spatial;

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
///   <li>浮点运算顺序与迁移前 <c>VoxelTerrainGen</c> 逐字保持一致（黄金地形哈希不变）。</li>
///   <li><see cref="Band"/> 与 HLSL <c>GetNode</c> 共用同一组常量与判据顺序。</li>
/// </ul>
/// </summary>
public sealed class TerrainHeightModel
{
    private readonly int _seed;
    private readonly TerrainConstants _constants;

    public TerrainHeightModel(in TerrainProfile profile, int seed, int unitsPerMeter = 100)
    {
        _seed = seed;
        _constants = TerrainConstants.From(profile, unitsPerMeter);

        var hash = SignatureHash.Begin();
        hash = SignatureHash.Mix(hash, seed);
        hash = SignatureHash.Mix(hash, _constants.UnitsPerMeter);
        hash = SignatureHash.Mix(hash, profile.SeaLevel);
        hash = SignatureHash.Mix(hash, profile.SoilDepth);
        hash = SignatureHash.Mix(hash, profile.HeightScale);
        hash = SignatureHash.Mix(hash, profile.HeightOffset);
        Signature = hash;
    }

    public TerrainConstants Constants => _constants;

    public int Seed => _seed;

    public int UnitsPerMeter => _constants.UnitsPerMeter;

    /// <summary>公式签名：seed + 全部地形常量 + 单位；参数不变则签名不变。</summary>
    public long Signature { get; }

    /// <summary>高度（cm，未取整）：与 GPU <c>HeightOffset + ComputeHeight(x,z) * HeightScale</c> 同式同序。</summary>
    public float HeightCm(int x, int z) =>
        _constants.HeightOffsetCm
        + TerrainNoise.ComputeHeight(x, z, _seed, _constants.UnitsPerMeter) * _constants.HeightScaleCm;

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

        return minGround == int.MaxValue ? HeightSpan.Empty : new HeightSpan(minGround, maxGround);
    }

    /// <summary>
    /// 水平 footprint 内「地表附近可产生可见面」的高度带：地表 ~ 地表向下 SoilDepth（土层）。
    /// <para>不含深层岩石（一直到底，但看不见）与水体（内部无面，只会挤占可见配额），
    /// 因此它是「实心表面」而非「所有非空气内容」。</para>
    /// <paramref name="marginCm"/> 覆盖采样步长内的插值起伏；逐 chunk 查询时传 0，避免把上方空气块带进候选。
    /// </summary>
    public HeightSpan SurfaceSpan(int minX, int minZ, int maxX, int maxZ, int marginCm = 0)
    {
        var ground = GroundSpan(minX, minZ, maxX, maxZ);
        if (ground.IsEmpty) return HeightSpan.Empty;

        int minY = ground.MinY - _constants.SoilDepthCm;
        int maxY = ground.MaxY;
        return marginCm > 0 ? new HeightSpan(minY, maxY).Expand(marginCm) : new HeightSpan(minY, maxY);
    }
}
