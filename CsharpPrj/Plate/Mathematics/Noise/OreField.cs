using MathematicalAlgorithms.Core;

namespace MathematicalAlgorithms.Noise;

/// <summary>
/// 矿脉分带参数（纯算法数据，不含方块 ID）：深度区间 + 噪声团簇尺度 + 阈值。
/// <para>同一深度可有多条带；按列表顺序取第一个命中（浅层矿优先），顺序即优先级。</para>
/// </summary>
public readonly record struct OreBand(
    int MinDepthMeters,
    int MaxDepthMeters,
    int CellMeters,
    float Threshold,
    int SeedSalt);

/// <summary>
/// 按深度分带 + 3D 噪声团簇的矿脉判定：世界坐标求值，跨 chunk 边界连续。
/// <para>团簇是 <see cref="NoisePrimitives.ValueNoise3D"/> 的等值面块，不是逐格随机。</para>
/// </summary>
public sealed class OreField
{
    private readonly OreBand[] _bands;
    private readonly int[] _cellsCm;
    private readonly int[] _minDepthCm;
    private readonly int[] _maxDepthCm;
    private readonly int[] _seeds;
    private readonly int _seed;

    public OreField(IReadOnlyList<OreBand> bands, int seed, int unitsPerMeter)
    {
        ArgumentNullException.ThrowIfNull(bands);
        if (unitsPerMeter <= 0)
            throw new ArgumentOutOfRangeException(nameof(unitsPerMeter), "单位/米必须为正");

        _bands = bands.ToArray();
        _cellsCm = new int[_bands.Length];
        _minDepthCm = new int[_bands.Length];
        _maxDepthCm = new int[_bands.Length];
        _seeds = new int[_bands.Length];
        _seed = seed;

        for (int i = 0; i < _bands.Length; i++)
        {
            var band = _bands[i];
            if (band.CellMeters <= 0)
                throw new ArgumentOutOfRangeException(nameof(bands), "矿石团簇波长必须为正");
            if (band.MinDepthMeters < 0 || band.MaxDepthMeters <= band.MinDepthMeters)
                throw new ArgumentOutOfRangeException(nameof(bands), "矿石深度区间非法");

            _cellsCm[i] = band.CellMeters * unitsPerMeter;
            _minDepthCm[i] = band.MinDepthMeters * unitsPerMeter;
            _maxDepthCm[i] = band.MaxDepthMeters * unitsPerMeter;
            _seeds[i] = seed ^ band.SeedSalt;
        }

        var hash = SignatureHash.Begin();
        hash = SignatureHash.Mix(hash, seed);
        foreach (var band in _bands)
        {
            hash = SignatureHash.Mix(hash, band.MinDepthMeters);
            hash = SignatureHash.Mix(hash, band.MaxDepthMeters);
            hash = SignatureHash.Mix(hash, band.CellMeters);
            hash = SignatureHash.Mix(hash, band.Threshold);
            hash = SignatureHash.Mix(hash, band.SeedSalt);
        }

        Signature = hash;
    }

    public IReadOnlyList<OreBand> Bands => _bands;

    public long Signature { get; }

    /// <summary>返回命中的矿带下标；未命中返回 -1。</summary>
    public int BandAt(int x, int y, int z, int groundYCm)
    {
        int depth = groundYCm - y;
        for (int i = 0; i < _bands.Length; i++)
        {
            if (depth < _minDepthCm[i] || depth > _maxDepthCm[i])
                continue;

            float cell = _cellsCm[i];
            float noise = NoisePrimitives.ValueNoise3D(
                x / cell, y / cell, z / cell, _seeds[i]);
            if (noise > _bands[i].Threshold)
                return i;
        }

        return -1;
    }
}
