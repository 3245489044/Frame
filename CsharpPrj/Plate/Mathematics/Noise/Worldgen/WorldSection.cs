using MathematicalAlgorithms.Spatial;

namespace MathematicalAlgorithms.Noise;

/// <summary>
/// 世界生成内容材质（角色语义，不含任何节点 ID）。
/// <para>Frame 只认材质；「材质 → 不透明方块 ID」由上层 <see cref="IBlockResolver"/> 负责。</para>
/// </summary>
public enum WorldMaterial : byte
{
    /// <summary>尚未被任何 section 写入（流水线起点）。只有地表 section 可以从 None 产出内容。</summary>
    None = 0,
    Air = 1,
    Stone = 2,
    Dirt = 3,
    Grass = 4,
    Sand = 5,
    Water = 6,
    Log = 7,
    Leaves = 8,
    Snow = 9,
    /// <summary>矿石；<see cref="WorldVoxel.Variant"/> 为矿带下标。</summary>
    Ore = 10,
}

/// <summary>世界生成输出体素（材质 + 变体下标），与引擎方块节点解耦。</summary>
public readonly record struct WorldVoxel(WorldMaterial Material, int Variant)
{
    public static WorldVoxel None { get; } = new(WorldMaterial.None, 0);

    public static WorldVoxel Air { get; } = new(WorldMaterial.Air, 0);

    public static WorldVoxel Of(WorldMaterial material) => new(material, 0);

    public static WorldVoxel Ore(int bandIndex) => new(WorldMaterial.Ore, bandIndex);

    public bool IsNone => Material == WorldMaterial.None;
}

/// <summary>
/// 单个体素的流水线输入：世界坐标 + 该列地表 + 该列群系（由调用方每列算一次）+ 上一节结果。
/// </summary>
public readonly record struct WorldSectionInput(
    int X,
    int Y,
    int Z,
    int GroundYCm,
    BiomeSample Biome,
    WorldVoxel Current);

/// <summary>
/// 可分节替换的世界生成 section。
/// <ul>
///   <li><see cref="Stage"/> 唯一且升序执行；覆盖关系必须写在 <see cref="Apply"/> 的材质判据里，不允许隐式依赖执行先后。</li>
///   <li><see cref="HeightSpanFor"/> 声明本节的垂直影响范围；流水线把各节范围并集交给高度模型，供可见性候选使用。</li>
/// </ul>
/// </summary>
public interface IWorldSection
{
    string Name { get; }

    int Stage { get; }

    long Signature { get; }

    /// <summary>在 [groundMin, groundMax] 的 footprint 内，本节可能写入内容的垂直范围（cm）。</summary>
    HeightSpan HeightSpanFor(int groundMinCm, int groundMaxCm, int seaLevelCm);

    /// <summary>按 input.Current 决定是否改写；返回 false 表示保持不变（<paramref name="output"/> 等于输入）。</summary>
    bool Apply(in WorldSectionInput input, out WorldVoxel output);
}

/// <summary>声明垂直影响范围的数据源（流水线本身也是该接口的实现）。</summary>
public interface IHeightSpanSource
{
    long Signature { get; }

    HeightSpan HeightSpanFor(int groundMinCm, int groundMaxCm, int seaLevelCm);
}

/// <summary>固定阶段常量：顺序即覆盖优先级（后面的节只能改写前面留下的明确材质）。</summary>
public static class WorldSectionStages
{
    public const int Terrain = 0;
    public const int Caves = 10;
    public const int Ores = 20;
    public const int Vegetation = 30;
}

/// <summary>群系地表材质（纯内容语义）：地表 / 亚地表。</summary>
public readonly record struct SurfaceRole(WorldMaterial Surface, WorldMaterial SubSurface)
{
    /// <summary>把声明面的方块角色名映射为内容材质；无法识别 ⇒ 抛（不静默猜测）。</summary>
    public static SurfaceRole FromRoles(string surfaceRole, string subSurfaceRole) =>
        new(RoleMaterial(surfaceRole), RoleMaterial(subSurfaceRole));

    private static WorldMaterial RoleMaterial(string role) => role switch
    {
        "grass" => WorldMaterial.Grass,
        "dirt" => WorldMaterial.Dirt,
        "sand" => WorldMaterial.Sand,
        "snow" => WorldMaterial.Snow,
        "stone" => WorldMaterial.Stone,
        "water" => WorldMaterial.Water,
        _ => throw new ArgumentException($"未知地表材质角色 '{role}'（支持 grass/dirt/sand/snow/stone/water）", nameof(role)),
    };
}
