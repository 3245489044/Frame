using MathematicalAlgorithms.Core;

namespace MathematicalAlgorithms.Noise;

/// <summary>
/// 群系气候参数（纯数据，不含方块 ID）：温度/湿度中心点决定气候分区，幅度/偏移决定高度倾向。
/// <para>新增群系 = 往列表里加一项 + 在 Worldgen 侧补一条地表规则；不改任何采样代码。</para>
/// </summary>
public readonly record struct BiomeClimate(
    string Name,
    float Temperature,
    float Humidity,
    float HeightAmplitude,
    float HeightBiasCm);

/// <summary>
/// 一次气候采样结果：最近两个群系及其权重（用于高度幅度的平滑过渡，避免群系边界出现台阶）。
/// <para><see cref="PrimaryWeight"/> 是最近群系的权重；边界处约 0.5，群系中心处趋近 1。</para>
/// </summary>
public readonly struct BiomeSample
{
    public readonly int Primary;
    public readonly int Secondary;
    public readonly float PrimaryWeight;
    public readonly float Temperature;
    public readonly float Humidity;

    public BiomeSample(int primary, int secondary, float primaryWeight, float temperature, float humidity)
    {
        Primary = primary;
        Secondary = secondary;
        PrimaryWeight = primaryWeight;
        Temperature = temperature;
        Humidity = humidity;
    }
}

/// <summary>
/// 群系气候场：温度/湿度两张 2D 值噪声（世界坐标求值，跨 chunk 连续），
/// 在气候平面里取最近两个群系中心做 Voronoi 加权。
/// <para>确定性：所有采样只依赖 (x,z,seed)；无 <see cref="Random"/>，无缓存状态。</para>
/// <para>尺度：配置以米传入，内部换算 cm，与 <see cref="TerrainNoise"/> 的坐标口径一致。</para>
/// </summary>
public sealed class BiomeField
{
    private readonly BiomeClimate[] _biomes;
    private readonly int _temperatureCellCm;
    private readonly int _humidityCellCm;
    private readonly int _temperatureSeed;
    private readonly int _humiditySeed;

    public BiomeField(
        IReadOnlyList<BiomeClimate> biomes,
        int seed,
        int unitsPerMeter = 100,
        int temperatureCellMeters = 50,
        int humidityCellMeters = 35)
    {
        ArgumentNullException.ThrowIfNull(biomes);
        if (biomes.Count == 0)
            throw new ArgumentException("群系表不能为空", nameof(biomes));
        if (unitsPerMeter <= 0)
            throw new ArgumentOutOfRangeException(nameof(unitsPerMeter), "单位/米必须为正");
        if (temperatureCellMeters <= 0 || humidityCellMeters <= 0)
            throw new ArgumentOutOfRangeException(nameof(temperatureCellMeters), "气候噪声波长必须为正");

        _biomes = biomes.ToArray();
        foreach (var biome in _biomes)
        {
            if (string.IsNullOrWhiteSpace(biome.Name))
                throw new ArgumentException("群系名不能为空", nameof(biomes));
            if (biome.Temperature is < 0f or > 1f || biome.Humidity is < 0f or > 1f)
                throw new ArgumentException($"群系 '{biome.Name}' 气候坐标须在 0..1", nameof(biomes));
        }

        for (int i = 0; i < _biomes.Length; i++)
            for (int j = i + 1; j < _biomes.Length; j++)
                if (_biomes[i].Name == _biomes[j].Name)
                    throw new ArgumentException($"群系名 '{_biomes[i].Name}' 重复", nameof(biomes));

        _temperatureCellCm = temperatureCellMeters * unitsPerMeter;
        _humidityCellCm = humidityCellMeters * unitsPerMeter;
        _temperatureSeed = seed ^ 0x5A17;
        _humiditySeed = seed ^ 0x2C0F;

        var hash = SignatureHash.Begin();
        hash = SignatureHash.Mix(hash, _temperatureCellCm);
        hash = SignatureHash.Mix(hash, _humidityCellCm);
        hash = SignatureHash.Mix(hash, _temperatureSeed);
        hash = SignatureHash.Mix(hash, _humiditySeed);
        foreach (var biome in _biomes)
        {
            hash = SignatureHash.Mix(hash, biome.Name.GetHashCode(StringComparison.Ordinal));
            hash = SignatureHash.Mix(hash, biome.Temperature);
            hash = SignatureHash.Mix(hash, biome.Humidity);
            hash = SignatureHash.Mix(hash, biome.HeightAmplitude);
            hash = SignatureHash.Mix(hash, biome.HeightBiasCm);
        }

        Signature = hash;
    }

    /// <summary>群系表（索引即 <see cref="BiomeSample.Primary"/> 的取值域）。</summary>
    public IReadOnlyList<BiomeClimate> Biomes => _biomes;

    /// <summary>气候尺度/群系参数签名；参数不变则签名不变。</summary>
    public long Signature { get; }

    /// <summary>在 (x,z) 处采样气候与最近两个群系（世界坐标，任意 chunk 求值一致）。</summary>
    public BiomeSample Sample(int x, int z)
    {
        float temperature = Normalize(TerrainNoise.ValueNoise(x, z, _temperatureCellCm, _temperatureSeed));
        float humidity = Normalize(TerrainNoise.ValueNoise(x, z, _humidityCellCm, _humiditySeed));

        int primary = 0;
        int secondary = 0;
        float best = float.MaxValue;
        float second = float.MaxValue;

        for (int i = 0; i < _biomes.Length; i++)
        {
            float dt = temperature - _biomes[i].Temperature;
            float dh = humidity - _biomes[i].Humidity;
            float distance = dt * dt + dh * dh;

            if (distance < best)
            {
                second = best;
                secondary = primary;
                best = distance;
                primary = i;
            }
            else if (distance < second)
            {
                second = distance;
                secondary = i;
            }
        }

        if (secondary == primary)
            secondary = _biomes.Length > 1 ? (primary == 0 ? 1 : 0) : primary;

        float weight = best + second > 0f ? second / (best + second) : 1f;
        return new BiomeSample(primary, secondary, weight, temperature, humidity);
    }

    private static float Normalize(float noise) => noise * 0.5f + 0.5f;
}
