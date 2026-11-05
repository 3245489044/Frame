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

    // 线程私有角值 scratch（每条带一条 lattice），与实例一一对应。
    private readonly ThreadLocal<OreScratch> _scratch = new(static () => new OreScratch());

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

        MinDepthCm = _bands.Length == 0 ? 0 : _minDepthCm.Min();
        MaxDepthCm = _bands.Length == 0 ? 0 : _maxDepthCm.Max();
    }

    public IReadOnlyList<OreBand> Bands => _bands;

    public long Signature { get; }

    /// <summary>全部矿带的最小起始深度（cm，相对地表）。</summary>
    public int MinDepthCm { get; }

    /// <summary>全部矿带的最大深度（cm，相对地表）。</summary>
    public int MaxDepthCm { get; }

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

    /// <summary>
    /// 为一个 chunk 预取各条带的角值，返回可逐体素求值的 sampler（与单点 <see cref="BandAt"/> 逐位同值）。
    /// <para>sampler 只对本次调用线程、最近一次 Prepare 有效；下一次调用会覆盖同一线程 scratch。</para>
    /// </summary>
    public OreSampler PrepareChunk(int originX, int originY, int originZ, int size)
    {
        if (size <= 0)
            throw new ArgumentOutOfRangeException(nameof(size), "chunk 边长必须为正");

        var scratch = _scratch.Value!;
        scratch.Ensure(_bands.Length);
        for (int i = 0; i < _bands.Length; i++)
            scratch.Lattices[i].Prepare(originX, originY, originZ, size, _cellsCm[i], _seeds[i]);

        return new OreSampler(this, scratch.Lattices, _bands.Length);
    }

    private sealed class OreScratch
    {
        public LatticeNoise3D[] Lattices = Array.Empty<LatticeNoise3D>();

        public void Ensure(int count)
        {
            if (Lattices.Length >= count)
                return;

            var grown = new LatticeNoise3D[Math.Max(count, 4)];
            Array.Copy(Lattices, grown, Lattices.Length);
            for (int i = Lattices.Length; i < grown.Length; i++)
                grown[i] = new LatticeNoise3D();
            Lattices = grown;
        }
    }

    /// <summary>chunk 级矿带判定（角值已预取）；判据与单点 <see cref="OreField.BandAt"/> 逐字相同。</summary>
    public readonly struct OreSampler
    {
        private readonly OreField _owner;
        private readonly LatticeNoise3D[] _lattices;
        private readonly int _count;

        internal OreSampler(OreField owner, LatticeNoise3D[] lattices, int count)
        {
            _owner = owner;
            _lattices = lattices;
            _count = count;
        }

        /// <summary>返回命中的矿带下标；未命中返回 -1（严格按 <see cref="OreField.Bands"/> 顺序，首个命中即返回）。</summary>
        public int BandAt(int localX, int localY, int localZ, int worldY, int groundYCm)
        {
            int depth = groundYCm - worldY;
            for (int i = 0; i < _count; i++)
            {
                if (depth < _owner._minDepthCm[i] || depth > _owner._maxDepthCm[i])
                    continue;

                if (_lattices[i].Sample(localX, localY, localZ) > _owner._bands[i].Threshold)
                    return i;
            }

            return -1;
        }
    }
}
