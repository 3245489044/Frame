using MathematicalAlgorithms.Core;

namespace MathematicalAlgorithms.Noise;

/// <summary>
/// GPU 高度采样契约（纯数据 + 参考实现，供渲染侧照搬进 HLSL）。
/// <ul>
///   <li>不含洞穴/矿脉/植被：只服务「高度 → 地表 Y」这一条公式。</summary>
///   <li>扁平数组布局固定，长度恒为 16 字节整数倍，可直接塞常量缓冲（见 <see cref="ToFlatFloats"/>）。</li>
///   <li><see cref="SampleHeight"/> 与 CPU <see cref="TerrainHeightModel.HeightCm"/> 逐位一致（同一 Voronoi/加权顺序）。</li>
/// </ul>
/// </summary>
public sealed class WorldHeightContract
{
    /// <summary>头部 float 数（8 × 4B = 32B，16B 对齐）。</summary>
    public const int HeaderFloats = 8;

    /// <summary>每个群系 float 数（4 × 4B = 16B，16B 对齐）。</summary>
    public const int BiomeFloats = 4;

    private readonly int _unitsPerMeter;
    private readonly int _seaLevelCm;
    private readonly float _heightScaleCm;
    private readonly float _heightOffsetCm;
    private readonly int _temperatureCellCm;
    private readonly int _humidityCellCm;
    private readonly int _seed;
    private readonly BiomeClimate[] _biomes;

    private WorldHeightContract(
        int unitsPerMeter,
        int seaLevelCm,
        float heightScaleCm,
        float heightOffsetCm,
        int temperatureCellCm,
        int humidityCellCm,
        int seed,
        BiomeClimate[] biomes)
    {
        _unitsPerMeter = unitsPerMeter;
        _seaLevelCm = seaLevelCm;
        _heightScaleCm = heightScaleCm;
        _heightOffsetCm = heightOffsetCm;
        _temperatureCellCm = temperatureCellCm;
        _humidityCellCm = humidityCellCm;
        _seed = seed;
        _biomes = biomes;

        var hash = SignatureHash.Begin();
        hash = SignatureHash.Mix(hash, unitsPerMeter);
        hash = SignatureHash.Mix(hash, seaLevelCm);
        hash = SignatureHash.Mix(hash, heightScaleCm);
        hash = SignatureHash.Mix(hash, heightOffsetCm);
        hash = SignatureHash.Mix(hash, temperatureCellCm);
        hash = SignatureHash.Mix(hash, humidityCellCm);
        hash = SignatureHash.Mix(hash, seed);
        foreach (var biome in biomes)
        {
            hash = SignatureHash.Mix(hash, biome.Temperature);
            hash = SignatureHash.Mix(hash, biome.Humidity);
            hash = SignatureHash.Mix(hash, biome.HeightAmplitude);
            hash = SignatureHash.Mix(hash, biome.HeightBiasCm);
        }

        Signature = hash;
    }

    public int BiomeCount => _biomes.Length;

    public long Signature { get; }

    /// <summary>扁平参数包长度（float 个数）；16 字节对齐。</summary>
    public int FloatCount => HeaderFloats + BiomeFloats * _biomes.Length;

    /// <summary>
    /// 从 CPU 地形模型 + 群系场导出参数包。
    /// </summary>
    public static WorldHeightContract From(in TerrainHeightModel model, BiomeField biomes)
    {
        ArgumentNullException.ThrowIfNull(biomes);

        var climates = new BiomeClimate[biomes.Biomes.Count];
        for (int i = 0; i < climates.Length; i++)
            climates[i] = biomes.Biomes[i];

        return new WorldHeightContract(
            model.UnitsPerMeter,
            model.Constants.SeaLevelCm,
            model.Constants.HeightScaleCm,
            model.Constants.HeightOffsetCm,
            biomes.TemperatureCellCm,
            biomes.HumidityCellCm,
            model.Seed,
            climates);
    }

    /// <summary>
    /// 扁平化常量缓冲（浮点序）：
    /// <code>
    /// [0] unitsPerMeter [1] seaLevelCm [2] heightScaleCm [3] heightOffsetCm
    /// [4] temperatureCellCm [5] humidityCellCm [6] seed [7] biomeCount
    /// [8 + 4i + 0] temperature [8 + 4i + 1] humidity [8 + 4i + 2] amplitude [8 + 4i + 3] biasCm
    /// </code>
    /// 长度 = 8 + 4N，恒为 16 字节整数倍。
    /// </summary>
    public float[] ToFlatFloats()
    {
        var flat = new float[FloatCount];
        flat[0] = _unitsPerMeter;
        flat[1] = _seaLevelCm;
        flat[2] = _heightScaleCm;
        flat[3] = _heightOffsetCm;
        flat[4] = _temperatureCellCm;
        flat[5] = _humidityCellCm;
        flat[6] = _seed;
        flat[7] = _biomes.Length;

        for (int i = 0; i < _biomes.Length; i++)
        {
            int offset = HeaderFloats + BiomeFloats * i;
            flat[offset] = _biomes[i].Temperature;
            flat[offset + 1] = _biomes[i].Humidity;
            flat[offset + 2] = _biomes[i].HeightAmplitude;
            flat[offset + 3] = _biomes[i].HeightBiasCm;
        }

        return flat;
    }

    /// <summary>从扁平数组还原（长度不符直接抛，不做静默补零）。</summary>
    public static WorldHeightContract FromFlatFloats(ReadOnlySpan<float> flat)
    {
        if (flat.Length < HeaderFloats)
            throw new ArgumentException($"高度参数包至少 {HeaderFloats} 个 float，实际 {flat.Length}", nameof(flat));

        int count = (int)flat[7];
        if (count < 0 || flat.Length != HeaderFloats + BiomeFloats * count)
            throw new ArgumentException(
                $"高度参数包长度应为 {HeaderFloats + BiomeFloats * count}（群系 {count}），实际 {flat.Length}", nameof(flat));

        var biomes = new BiomeClimate[count];
        for (int i = 0; i < count; i++)
        {
            int offset = HeaderFloats + BiomeFloats * i;
            biomes[i] = new BiomeClimate(
                Name: $"biome{i}",
                Temperature: flat[offset],
                Humidity: flat[offset + 1],
                HeightAmplitude: flat[offset + 2],
                HeightBiasCm: flat[offset + 3]);
        }

        return new WorldHeightContract(
            (int)flat[0], (int)flat[1], flat[2], flat[3],
            (int)flat[4], (int)flat[5], (int)flat[6], biomes);
    }

    /// <summary>
    /// 参考实现（CPU 侧）：与 <see cref="TerrainHeightModel.HeightCm"/> 逐位一致。
    /// <para>HLSL 照搬要点：</para>
    /// <list type="number">
    ///   <item>温度/湿度 = 值噪声 (cell = temperatureCellCm / humidityCellCm, seed = seed ^ 0x5A17 / 0x2C0F)，再 *0.5+0.5。</item>
    ///   <item>在气候平面里取最近两个群系：distance = (t - Ti)² + (h - Hi)²；best/second 严格比较。</item>
    ///   <item>weight = second / (best + second)（两者都为 0 时取 1）。</item>
    ///   <item>高度幅度/偏移按 weight 线性插值；height = offset + bias + noise * scale * amplitude。</item>
    /// </list>
    /// </summary>
    public float SampleHeight(int x, int z)
    {
        float noise = TerrainNoise.ComputeHeight(x, z, _seed, _unitsPerMeter);
        float temperature = BiomeField.NormalizeClimate(
            TerrainNoise.ValueNoise(x, z, _temperatureCellCm, _seed ^ BiomeField.TemperatureSeedSalt));
        float humidity = BiomeField.NormalizeClimate(
            TerrainNoise.ValueNoise(x, z, _humidityCellCm, _seed ^ BiomeField.HumiditySeedSalt));

        float weight = BiomeField.PrimaryWeight(_biomes, temperature, humidity, out int primary, out int secondary);
        var sample = new BiomeSample(primary, secondary, weight, temperature, humidity);

        var constants = new TerrainConstants(
            UnitsPerMeter: _unitsPerMeter,
            SeaLevelCm: _seaLevelCm,
            SoilDepthCm: 0,
            HeightScaleCm: _heightScaleCm,
            HeightOffsetCm: _heightOffsetCm,
            SandBandCm: _unitsPerMeter,
            NoiseCell16: 16 * _unitsPerMeter,
            NoiseCell8: 8 * _unitsPerMeter,
            NoiseCell4: 4 * _unitsPerMeter);

        return HeightFormula.Evaluate(constants, _biomes, sample, noise);
    }
}
