using MathematicalAlgorithms.Core;
using Modules.Compute.Formula;

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
/// <para>确定性：所有采样只依赖 (x,z,seed)；无 <see cref="Random"/>。</para>
/// <para>尺度：配置以米传入，内部换算 cm，与 <see cref="TerrainNoise"/> 的坐标口径一致。</para>
/// <para>
/// <b>角值缓存（只省哈希，不改数值）</b>：气候噪声是「lattice 角值 + 双线性插值」，
/// 同一气候格（<c>temperatureCellCm</c> / <c>humidityCellCm</c> 那级）内的所有列共享同一组角值。
/// 缓存按 <c>(字段种子, 波长, 格号cx, 格号cz)</c> 命中，<b>缓存返回的是同一次插值所用的原始角值</b>，
/// 因此 <see cref="Sample"/> 的输出与原逐点直算逐位相同（不是「重算结果相同」）。
/// </para>
/// <para>
/// <b>作用域与失效</b>：缓存属于 <see cref="BiomeField"/> 实例，跨 chunk / 跨 tile 复用；
/// 实例构造后波长、种子、群系表都是只读（<see cref="Signature"/> 已把它们全部混入），
/// 因此已在缓存中的角值不可能过期——要改配置只能新建实例（另一实例另一份缓存）。
/// 容量是软上限，满时整表清空（只影响命中率/耗时，不影响数值）。
/// </para>
/// </summary>
public sealed class BiomeField
{
    /// <summary>温度噪声 seed 盐（CPU 与高度契约共用）。</summary>
    public const int TemperatureSeedSalt = 0x5A17;

    /// <summary>湿度噪声 seed 盐（CPU 与高度契约共用）。</summary>
    public const int HumiditySeedSalt = 0x2C0F;

    private readonly BiomeClimate[] _biomes;
    private readonly int _temperatureCellCm;
    private readonly int _humidityCellCm;
    private readonly int _temperatureSeed;
    private readonly int _humiditySeed;

    // 气候角值缓存：每张气候噪声独立签名（种子 + 波长），键 (签名, 格号cx, 波长, 格号cz)。
    private readonly TileCache _climateCorners;
    private readonly long _temperatureKey;
    private readonly long _humidityKey;

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
        _temperatureSeed = seed ^ TemperatureSeedSalt;
        _humiditySeed = seed ^ HumiditySeedSalt;

        // 缓存键签名：与字段种子 + 波长绑定（群系只影响权重选择，不影响角值）。
        _temperatureKey = SignatureSet.Mix(SignatureSet.Mix(SignatureSet.Seed, _temperatureSeed), _temperatureCellCm);
        _humidityKey = SignatureSet.Mix(SignatureSet.Mix(SignatureSet.Seed, _humiditySeed), _humidityCellCm);
        _climateCorners = new TileCache();

        var hash = SignatureHash.Begin();
        hash = SignatureHash.Mix(hash, _temperatureCellCm);
        hash = SignatureHash.Mix(hash, _humidityCellCm);
        hash = SignatureHash.Mix(hash, _temperatureSeed);
        hash = SignatureHash.Mix(hash, _humiditySeed);
        foreach (var biome in _biomes)
        {
            hash = SignatureHash.Mix(hash, StableNameHash(biome.Name));
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

    /// <summary>温度噪声波长（cm）。</summary>
    public int TemperatureCellCm => _temperatureCellCm;

    /// <summary>湿度噪声波长（cm）。</summary>
    public int HumidityCellCm => _humidityCellCm;

    /// <summary>在 (x,z) 处采样气候与最近两个群系（世界坐标，任意 chunk 求值一致）。</summary>
    public BiomeSample Sample(int x, int z)
    {
        float temperature = NormalizeClimate(
            TerrainNoise.ValueNoise(x, z, _temperatureCellCm, _temperatureSeed));
        float humidity = NormalizeClimate(
            TerrainNoise.ValueNoise(x, z, _humidityCellCm, _humiditySeed));

        float weight = PrimaryWeight(_biomes, temperature, humidity, out int primary, out int secondary);
        return new BiomeSample(primary, secondary, weight, temperature, humidity);
    }

    /// <summary>气候噪声归一化到 0..1（CPU 与高度契约共用）。</summary>
    public static float NormalizeClimate(float noise) => noise * 0.5f + 0.5f;

    /// <summary>按气候坐标取最近两个群系与权重（高度契约与 CPU 共用同一判据与顺序）。</summary>
    public static float PrimaryWeight(
        IReadOnlyList<BiomeClimate> biomes, float temperature, float humidity,
        out int primary, out int secondary)
    {
        ArgumentNullException.ThrowIfNull(biomes);
        if (biomes.Count == 0)
            throw new ArgumentException("群系表不能为空", nameof(biomes));

        primary = 0;
        secondary = 0;
        float best = float.MaxValue;
        float second = float.MaxValue;

        for (int i = 0; i < biomes.Count; i++)
        {
            float dt = temperature - biomes[i].Temperature;
            float dh = humidity - biomes[i].Humidity;
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
            secondary = biomes.Count > 1 ? (primary == 0 ? 1 : 0) : primary;

        return best + second > 0f ? second / (best + second) : 1f;
    }

    /// <summary>一次性取出最近两个群系的采样结果（高度契约与 CPU 共用）。</summary>
    public static BiomeSample Nearest(
        IReadOnlyList<BiomeClimate> biomes, float temperature, float humidity)
    {
        float weight = PrimaryWeight(biomes, temperature, humidity, out int primary, out int secondary);
        return new BiomeSample(primary, secondary, weight, temperature, humidity);
    }

    /// <summary>跨进程稳定的名称哈希（不用 <see cref="string.GetHashCode()"/>，避免随机化盐影响签名）。</summary>
    private static long StableNameHash(string name)
    {
        long hash = 0x51F3;
        foreach (char ch in name)
            hash = hash * 131 + ch;
        return hash;
    }
}
