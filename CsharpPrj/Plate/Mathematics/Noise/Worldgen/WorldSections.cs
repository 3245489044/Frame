using MathematicalAlgorithms.Core;
using MathematicalAlgorithms.Spatial;

namespace MathematicalAlgorithms.Noise;

/// <summary>
/// 地表分带 section（Stage 0）：唯一允许从 <see cref="WorldVoxel.None"/> 产出初始内容的节。
/// <para>判据仍是 <see cref="TerrainHeightModel.Band"/>；群系只决定 Grass/Dirt 用哪种地表材质。</para>
/// </summary>
public sealed class TerrainBandSection : IWorldSection
{
    private readonly TerrainHeightModel _model;
    private readonly SurfaceRole[] _surfaces;

    public TerrainBandSection(TerrainHeightModel model, IReadOnlyList<SurfaceRole> surfaces)
    {
        _model = model ?? throw new ArgumentNullException(nameof(model));
        ArgumentNullException.ThrowIfNull(surfaces);
        if (surfaces.Count != model.BiomeCount)
            throw new ArgumentException(
                $"地表规则数 {surfaces.Count} 与群系数 {model.BiomeCount} 不一致", nameof(surfaces));

        _surfaces = surfaces.ToArray();

        var hash = SignatureHash.Begin();
        hash = SignatureHash.Mix(hash, model.Signature);
        foreach (var surface in _surfaces)
        {
            hash = SignatureHash.Mix(hash, (int)surface.Surface);
            hash = SignatureHash.Mix(hash, (int)surface.SubSurface);
        }
        Signature = hash;
    }

    public string Name => "terrain-band";

    public int Stage => WorldSectionStages.Terrain;

    public long Signature { get; }

    public HeightSpan HeightSpanFor(int groundMinCm, int groundMaxCm, int seaLevelCm)
    {
        int minY = groundMinCm - _model.Constants.SoilDepthCm;
        int maxY = groundMaxCm;

        // ground <= SeaLevel 的列会在 (ground, SeaLevel] 生成水；其顶面是可见面。
        if (groundMinCm <= seaLevelCm)
            maxY = Math.Max(maxY, seaLevelCm);

        return new HeightSpan(minY, maxY);
    }

    public bool Apply(in WorldSectionInput input, out WorldVoxel output)
    {
        output = input.Current;
        if (!input.Current.IsNone)
            return false;

        var band = _model.Band(input.Y, input.GroundYCm);
        switch (band)
        {
            case TerrainBand.Air:
                output = WorldVoxel.Air;
                return true;

            case TerrainBand.Water:
                output = WorldVoxel.Of(WorldMaterial.Water);
                return true;

            case TerrainBand.Sand:
                output = WorldVoxel.Of(WorldMaterial.Sand);
                return true;

            case TerrainBand.Grass:
                output = WorldVoxel.Of(_surfaces[input.Biome.Primary].Surface);
                return true;

            case TerrainBand.Dirt:
                output = WorldVoxel.Of(_surfaces[input.Biome.Primary].SubSurface);
                return true;

            default: // Stone
                output = WorldVoxel.Of(WorldMaterial.Stone);
                return true;
        }
    }
}

/// <summary>洞穴 section（Stage 10）：只把实心 <see cref="WorldMaterial.Stone"/> 刻蚀成空气。</summary>
public sealed class CaveSection : IWorldSection
{
    private readonly CaveField _caves;

    public CaveSection(CaveField caves)
    {
        _caves = caves ?? throw new ArgumentNullException(nameof(caves));
        Signature = caves.Signature;
    }

    public string Name => "caves";

    public int Stage => WorldSectionStages.Caves;

    public long Signature { get; }

    public HeightSpan HeightSpanFor(int groundMinCm, int groundMaxCm, int seaLevelCm) =>
        new(groundMinCm - _caves.MaxDepthCm, groundMaxCm - _caves.MinDepthCm);

    public bool Apply(in WorldSectionInput input, out WorldVoxel output)
    {
        output = input.Current;
        if (input.Current.Material != WorldMaterial.Stone)
            return false;

        int depth = input.GroundYCm - input.Y;
        if (depth < _caves.MinDepthCm || depth > _caves.MaxDepthCm)
            return false;

        if (!_caves.IsCarved(input.X, input.Y, input.Z, input.GroundYCm))
            return false;

        output = WorldVoxel.Air;
        return true;
    }
}

/// <summary>矿脉 section（Stage 20）：只把（未被洞穴刻蚀的）<see cref="WorldMaterial.Stone"/> 替换为矿石。</summary>
public sealed class OreSection : IWorldSection
{
    private readonly OreField _ores;

    public OreSection(OreField ores)
    {
        _ores = ores ?? throw new ArgumentNullException(nameof(ores));
        Signature = ores.Signature;
    }

    public string Name => "ores";

    public int Stage => WorldSectionStages.Ores;

    public long Signature { get; }

    public HeightSpan HeightSpanFor(int groundMinCm, int groundMaxCm, int seaLevelCm) =>
        _ores.Bands.Count == 0
            ? HeightSpan.Empty
            : new HeightSpan(groundMinCm - _ores.MaxDepthCm, groundMaxCm - _ores.MinDepthCm);

    public bool Apply(in WorldSectionInput input, out WorldVoxel output)
    {
        output = input.Current;
        if (input.Current.Material != WorldMaterial.Stone)
            return false;

        int depth = input.GroundYCm - input.Y;
        if (depth < _ores.MinDepthCm || depth > _ores.MaxDepthCm)
            return false;

        int band = _ores.BandAt(input.X, input.Y, input.Z, input.GroundYCm);
        if (band < 0)
            return false;

        output = WorldVoxel.Ore(band);
        return true;
    }
}

/// <summary>植被 section（Stage 30）：只把地表以上的空气改写为树干/树叶（跨块按世界坐标求值）。</summary>
public sealed class VegetationSection : IWorldSection
{
    private readonly VegetationField _vegetation;
    private readonly int _maxTreeTopAboveGroundCm;

    public VegetationSection(VegetationField vegetation)
    {
        _vegetation = vegetation ?? throw new ArgumentNullException(nameof(vegetation));
        _maxTreeTopAboveGroundCm = vegetation.MaxTreeTopAboveGroundCm;
        Signature = vegetation.Signature;
    }

    public string Name => "vegetation";

    public int Stage => WorldSectionStages.Vegetation;

    public long Signature { get; }

    public HeightSpan HeightSpanFor(int groundMinCm, int groundMaxCm, int seaLevelCm) =>
        new(groundMinCm, groundMaxCm + _maxTreeTopAboveGroundCm);

    public bool Apply(in WorldSectionInput input, out WorldVoxel output)
    {
        output = input.Current;
        if (input.Current.Material != WorldMaterial.Air)
            return false;

        if (input.Y <= input.GroundYCm || input.Y > input.GroundYCm + _maxTreeTopAboveGroundCm)
            return false;

        return _vegetation.PartAt(input.X, input.Y, input.Z, input.GroundYCm) switch
        {
            TreePart.Trunk => Assign(out output, WorldVoxel.Of(WorldMaterial.Log)),
            TreePart.Leaves => Assign(out output, WorldVoxel.Of(WorldMaterial.Leaves)),
            _ => false,
        };
    }

    private static bool Assign(out WorldVoxel output, WorldVoxel value)
    {
        output = value;
        return true;
    }
}
