using MathematicalAlgorithms.Core;

namespace MathematicalAlgorithms.Noise;

/// <summary>
/// 洞穴噪声参数（纯算法数据，不含方块 ID）。
/// <para>隧道 = 组合 3D 值噪声的零等值面；洞厅 = 第二组低频 3D 噪声阈值。</para>
/// </summary>
public readonly record struct CaveSettings(
    int MinDepthMeters,
    int MaxDepthMeters,
    int TunnelCellMeters,
    float TunnelWidth,
    int CheeseCellMeters,
    float CheeseThreshold,
    int SurfaceGuardMeters,
    int SeedSalt)
{
    /// <summary>默认：6 m 以下开洞，隧道宽 0.03，深部大洞厅阈值 0.76；海床下 5 m 内留保护层。</summary>
    public static CaveSettings Default => new(6, 160, 9, 0.03f, 30, 0.76f, 5, 0x2B7E1);
}

/// <summary>
/// 3D 噪声刻蚀：按「距地表深度」调制，世界坐标求值。
/// <para>相邻 chunk 在边界处对同一 <c>(x,y,z)</c> 得到同一结果（不存在局部坐标）。</para>
/// <para>确定性：只用带 seed 的 <see cref="NoisePrimitives.ValueNoise3D"/>。</para>
/// </summary>
public sealed class CaveField
{
    private readonly CaveSettings _settings;
    private readonly int _seed;
    private readonly int _tunnelCellCm;
    private readonly int _cheeseCellCm;
    private readonly int _minDepthCm;
    private readonly int _maxDepthCm;
    private readonly int _guardCm;
    private readonly int _seaLevelCm;
    private readonly int _tunnelSeed;
    private readonly int _cheeseSeed;
    private readonly int _blendSeed;

    public CaveField(CaveSettings settings, int seed, int unitsPerMeter, int seaLevelCm)
    {
        if (unitsPerMeter <= 0)
            throw new ArgumentOutOfRangeException(nameof(unitsPerMeter), "单位/米必须为正");
        if (settings.TunnelCellMeters <= 0 || settings.CheeseCellMeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(settings), "洞穴噪声波长必须为正");
        if (settings.MinDepthMeters < 0 || settings.MaxDepthMeters <= settings.MinDepthMeters)
            throw new ArgumentOutOfRangeException(nameof(settings), "洞穴深度区间非法");

        _settings = settings;
        _seed = seed;
        _tunnelCellCm = settings.TunnelCellMeters * unitsPerMeter;
        _cheeseCellCm = settings.CheeseCellMeters * unitsPerMeter;
        _minDepthCm = settings.MinDepthMeters * unitsPerMeter;
        _maxDepthCm = settings.MaxDepthMeters * unitsPerMeter;
        _guardCm = settings.SurfaceGuardMeters * unitsPerMeter;
        _seaLevelCm = seaLevelCm;
        _tunnelSeed = seed ^ settings.SeedSalt;
        _cheeseSeed = seed ^ (settings.SeedSalt + 0x1471);
        _blendSeed = seed ^ (settings.SeedSalt + 0x39A5);

        var hash = SignatureHash.Begin();
        hash = SignatureHash.Mix(hash, seed);
        hash = SignatureHash.Mix(hash, settings.MinDepthMeters);
        hash = SignatureHash.Mix(hash, settings.MaxDepthMeters);
        hash = SignatureHash.Mix(hash, settings.TunnelCellMeters);
        hash = SignatureHash.Mix(hash, settings.TunnelWidth);
        hash = SignatureHash.Mix(hash, settings.CheeseCellMeters);
        hash = SignatureHash.Mix(hash, settings.CheeseThreshold);
        hash = SignatureHash.Mix(hash, settings.SurfaceGuardMeters);
        hash = SignatureHash.Mix(hash, settings.SeedSalt);
        Signature = hash;
    }

    public CaveSettings Settings => _settings;

    /// <summary>洞穴起始深度（cm，相对地表）。</summary>
    public int MinDepthCm => _minDepthCm;

    /// <summary>洞穴最大深度（cm，相对地表）。</summary>
    public int MaxDepthCm => _maxDepthCm;

    /// <summary>参数签名（seed + 全部洞穴标量）。</summary>
    public long Signature { get; }

    /// <summary>该格是否被洞穴刻蚀成空气（调用方负责只在实心带询问）。</summary>
    public bool IsCarved(int x, int y, int z, int groundYCm)
    {
        int depth = groundYCm - y;
        if (depth < _minDepthCm || depth > _maxDepthCm)
            return false;

        // 海床/湖床下方留保护层，避免把水直接开洞到水柱底部。
        if (groundYCm <= _seaLevelCm && depth < _minDepthCm + _guardCm)
            return false;

        // 接近地表时隧道收窄，避免地表被戳成筛子。
        float fade = MathF.Min(1f, (depth - _minDepthCm) / (float)_guardCm);
        float width = _settings.TunnelWidth * fade;

        float cell = _tunnelCellCm;
        float n1 = NoisePrimitives.ValueNoise3D(x / cell, y / cell, z / cell, _tunnelSeed);
        float n2 = NoisePrimitives.ValueNoise3D(x / cell, y / cell, z / cell, _blendSeed);
        float tunnel = MathF.Abs(0.65f * n1 + 0.35f * n2);

        // 隧道零等值面：两层噪声组合后的绝对值为小 ⇒ 形成互相连通的管状空腔。
        if (tunnel < width)
            return true;

        // 深部大洞厅：只在中深层生效。
        if (depth < _minDepthCm + _guardCm)
            return false;

        float cheeseCell = _cheeseCellCm;
        float cheese = NoisePrimitives.ValueNoise3D(
            x / cheeseCell, y / cheeseCell, z / cheeseCell, _cheeseSeed);
        return cheese > _settings.CheeseThreshold;
    }
}
