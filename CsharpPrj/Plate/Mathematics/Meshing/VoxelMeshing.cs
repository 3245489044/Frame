using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace MathematicalAlgorithms.Meshing;

/// <summary>
/// 网格化用的面剔除类别（三态 + 液体）。
/// <para><see cref="Air"/> = 0 使 <c>default(VoxelTraits)</c> 天然不渲染，未知 ID 安全回退为「不产生面」。</para>
/// <para><see cref="Auto"/> 仅为旧二元 traits 的兼容入口：由 <c>Opaque/Liquid</c> 推导。</para>
/// </summary>
public enum VoxelRenderClass
{
    /// <summary>空气 / 未注册 ID：任何面都不渲染。</summary>
    Air = 0,

    /// <summary>由 Opaque/Liquid 推导（旧调用点兼容）。</summary>
    Auto = 1,

    /// <summary>完全不透明：遮挡邻面。</summary>
    Opaque = 2,

    /// <summary>切图（树叶）：不遮挡邻面，与自身相邻仍渲染。</summary>
    Cutout = 3,

    /// <summary>半透明（玻璃）：只剔「同 ID」内面。</summary>
    Translucent = 4,

    /// <summary>液体（水/岩浆）：只剔「同 ID」内面。</summary>
    Liquid = 5,
}

/// <summary>
/// 方块 traits（网格化输入的唯一内容描述）：不透明/液体 + 三面图集编号 + 发光强度 + 剔除类别。
/// <para>索引即方块身份（节点 ID）；空气恒为索引 0 且全部为默认值。</para>
/// <para>本类型不编码任何游戏语义（草/土/水/硬度），只提供网格化所需的正交属性。</para>
/// <para><see cref="RenderClass"/> 缺省为 <see cref="VoxelRenderClass.Auto"/>，旧 6 参构造行为不变：
/// 不透明→不透明、液体→液体、其余→切图（现在会出面，此前被误判为不出面）。</para>
/// </summary>
public readonly record struct VoxelTraits(
    bool Opaque,
    bool Liquid,
    int TopTile,
    int SideTile,
    int BottomTile,
    int LightEmission,
    VoxelRenderClass RenderClass = VoxelRenderClass.Auto)
{
    /// <summary>实际生效的剔除类别（Auto 时按旧二元属性推导）。</summary>
    public VoxelRenderClass CullingClass
        => RenderClass != VoxelRenderClass.Auto
            ? RenderClass
            : Opaque
                ? VoxelRenderClass.Opaque
                : Liquid
                    ? VoxelRenderClass.Liquid
                    : VoxelRenderClass.Cutout;
}

/// <summary>
/// 网格顶点（冻结布局，48 字节 / 12 float）：POSITION / NORMAL / TEXCOORD0(uv) / TEXCOORD1(tile,light,ao,pad)。
/// <para>与引擎 Graphics 的 <c>VertexLayout.VoxelVertex()</c> 逐字段对应；任何字段调整都属于破坏性协议变更。</para>
/// </summary>
[StructLayout(LayoutKind.Sequential)]
public struct VoxelVertex
{
    public float X, Y, Z;
    public float NX, NY, NZ;
    public float U, V;
    public float Tile;
    public float Light;
    public float Ao;
    public float Pad;

    public VoxelVertex(float x, float y, float z,
        float nx, float ny, float nz,
        float u, float v, float tile, float light, float ao)
    {
        X = x; Y = y; Z = z;
        NX = nx; NY = ny; NZ = nz;
        U = u; V = v;
        Tile = tile; Light = light; Ao = ao; Pad = 0f;
    }
}

/// <summary>一个区块的网格结果（不透明与液体分开，便于深度/混合分 pass）。纯数据、可比较。</summary>
public sealed class VoxelMeshData
{
    public long Version { get; }
    public VoxelVertex[] OpaqueVertices { get; }
    public uint[] OpaqueIndices { get; }
    public VoxelVertex[] WaterVertices { get; }
    public uint[] WaterIndices { get; }

    public VoxelMeshData(
        long version,
        VoxelVertex[] opaqueVertices, uint[] opaqueIndices,
        VoxelVertex[] waterVertices, uint[] waterIndices)
    {
        Version = version;
        OpaqueVertices = opaqueVertices;
        OpaqueIndices = opaqueIndices;
        WaterVertices = waterVertices;
        WaterIndices = waterIndices;
    }

    public int OpaqueTriangles => OpaqueIndices.Length / 3;
    public int WaterTriangles => WaterIndices.Length / 3;
    public int TotalTriangles => OpaqueTriangles + WaterTriangles;
    public bool IsEmpty => OpaqueIndices.Length == 0 && WaterIndices.Length == 0;
}

/// <summary>
/// Greedy mesher（面剔除 + 同方块相邻面矩形合并 + AO/光照采样）。
/// <para>输入必须是 (chunkSize+2)³ 的 padded 快照：方块身份数组、光照数组、traits 表。</para>
/// <para>输出确定性：迭代顺序固定，不遍历字典；同一输入两次输出逐字节一致。</para>
/// <para>算法不含世界状态、不引用引擎类型；世界坐标原点由调用方给出。</para>
/// </summary>
public static class VoxelMesher
{
    /// <summary>光照等级上限（与方块光照传播算法同一口径）。</summary>
    public const int MaxLightLevel = 15;

    private const int NegativeFlag = 0x10000;
    private static readonly int[] CornerU = { 0, 1, 1, 0 };
    private static readonly int[] CornerV = { 0, 0, 1, 1 };

    public static VoxelMeshData Mesh(
        int chunkSize, long version,
        int originX, int originY, int originZ,
        ReadOnlySpan<ushort> paddedBlocks,
        ReadOnlySpan<byte> paddedLight,
        ReadOnlySpan<VoxelTraits> traits)
    {
        int dim = chunkSize + 2;
        int volume = dim * dim * dim;
        if (paddedBlocks.Length < volume)
            throw new ArgumentException("padded 数组不足 (chunkSize+2)^3", nameof(paddedBlocks));
        if (paddedLight.Length < volume)
            throw new ArgumentException("light 数组不足 (chunkSize+2)^3", nameof(paddedLight));

        var opaqueVertices = new List<VoxelVertex>(chunkSize * chunkSize);
        var opaqueIndices = new List<uint>(chunkSize * chunkSize * 6);
        var waterVertices = new List<VoxelVertex>(chunkSize * chunkSize);
        var waterIndices = new List<uint>(chunkSize * chunkSize * 6);

        int[] origin = { originX, originY, originZ };
        int[] pos = new int[3];
        int[] neighbor = new int[3];

        for (int d = 0; d < 3; d++)
        {
            int u = (d + 1) % 3;
            int v = (d + 2) % 3;
            var mask = new int[chunkSize * chunkSize];
            var used = new bool[chunkSize * chunkSize];

            for (int plane = 0; plane <= chunkSize; plane++)
            {
                Array.Clear(mask, 0, mask.Length);
                Array.Clear(used, 0, used.Length);

                for (int j = 0; j < chunkSize; j++)
                for (int i = 0; i < chunkSize; i++)
                {
                    pos[d] = plane;
                    pos[u] = i + 1;
                    pos[v] = j + 1;
                    ushort a = paddedBlocks[Index(pos, dim)];

                    pos[d] = plane + 1;
                    ushort b = paddedBlocks[Index(pos, dim)];

                    int cell = j * chunkSize + i;
                    if (plane == 0)
                    {
                        // a 属邻块，b 是本块 local 0：只允许 b 的 -d 面。
                        if (FaceVisible(b, a, traits))
                            mask[cell] = b | NegativeFlag;
                    }
                    else if (plane == chunkSize)
                    {
                        // a 是本块 local chunkSize-1，b 属邻块：只允许 a 的 +d 面。
                        if (FaceVisible(a, b, traits))
                            mask[cell] = a;
                    }
                    else if (FaceVisible(a, b, traits))
                    {
                        mask[cell] = a;
                    }
                    else if (FaceVisible(b, a, traits))
                    {
                        mask[cell] = b | NegativeFlag;
                    }
                }

                for (int j = 0; j < chunkSize; j++)
                for (int i = 0; i < chunkSize; i++)
                {
                    int cell = j * chunkSize + i;
                    int value = mask[cell];
                    if (value == 0 || used[cell]) continue;

                    int width = 1;
                    while (i + width < chunkSize)
                    {
                        int next = cell + width;
                        if (mask[next] != value || used[next]) break;
                        width++;
                    }

                    int height = 1;
                    while (j + height < chunkSize)
                    {
                        int row = (j + height) * chunkSize + i;
                        bool rowMatches = true;
                        for (int k = 0; k < width; k++)
                        {
                            if (mask[row + k] != value || used[row + k]) { rowMatches = false; break; }
                        }
                        if (!rowMatches) break;
                        height++;
                    }

                    for (int hh = 0; hh < height; hh++)
                    for (int ww = 0; ww < width; ww++)
                        used[(j + hh) * chunkSize + i + ww] = true;

                    bool negative = (value & NegativeFlag) != 0;
                    ushort id = (ushort)(value & 0xFFFF);
                    bool liquid = Liquid(id, traits);

                    neighbor[d] = negative ? plane : plane + 1;
                    neighbor[u] = i + 1;
                    neighbor[v] = j + 1;

                    int planeWorld = origin[d] + plane;
                    // 相邻 greedy 矩形尺寸不同会产生 T-junction 裂缝；向外扩 0.02 个世界单位覆盖裂缝。
                    float seamEpsilon = 0.02f;
                    int u0 = origin[u] + i;
                    int u1 = u0 + width;
                    int v0 = origin[v] + j;
                    int v1 = v0 + height;

                    int faceKind = d == 1 ? (negative ? 2 : 0) : 1;
                    float tile = TileFor(id, faceKind, traits);
                    float lightLevel = paddedLight[Index(neighbor, dim)] / (float)MaxLightLevel;

                    float[] ax = new float[4];
                    float[] ay = new float[4];
                    float[] az = new float[4];
                    float[] au = new float[4];
                    float[] av = new float[4];
                    float[] ao = new float[4];

                    // 四角按周长顺序（不是 Z 序），否则两个三角形会沿错误对角线分裂，产生三角空洞。
                    int ci = 0;
                    for (int k = 0; k < 4; k++)
                    {
                        int cu = CornerU[k];
                        int cv = CornerV[k];
                        float[] p3 = { 0f, 0f, 0f };
                        p3[d] = planeWorld;
                        p3[u] = cu == 0 ? u0 - seamEpsilon : u1 + seamEpsilon;
                        p3[v] = cv == 0 ? v0 - seamEpsilon : v1 + seamEpsilon;
                        ax[ci] = p3[0]; ay[ci] = p3[1]; az[ci] = p3[2];
                        au[ci] = cu * width; av[ci] = cv * height;
                        ao[ci] = ComputeAo(paddedBlocks, traits, neighbor, u, v,
                            cu == 0 ? -1 : 1, cv == 0 ? -1 : 1, dim);
                        ci++;
                    }

                    float nx = d == 0 ? (negative ? -1f : 1f) : 0f;
                    float ny = d == 1 ? (negative ? -1f : 1f) : 0f;
                    float nz = d == 2 ? (negative ? -1f : 1f) : 0f;

                    var verts = liquid ? waterVertices : opaqueVertices;
                    var inds = liquid ? waterIndices : opaqueIndices;
                    uint baseIndex = (uint)verts.Count;

                    for (int k = 0; k < 4; k++)
                        verts.Add(new VoxelVertex(
                            ax[k], ay[k], az[k], nx, ny, nz,
                            au[k], av[k], tile, lightLevel, ao[k] / 3f));

                    inds.Add(baseIndex + 0);
                    inds.Add(baseIndex + 1);
                    inds.Add(baseIndex + 2);
                    inds.Add(baseIndex + 0);
                    inds.Add(baseIndex + 2);
                    inds.Add(baseIndex + 3);
                }
            }
        }

        return new VoxelMeshData(
            version,
            opaqueVertices.ToArray(), opaqueIndices.ToArray(),
            waterVertices.ToArray(), waterIndices.ToArray());
    }

    private static VoxelTraits TraitsOf(ushort id, ReadOnlySpan<VoxelTraits> traits) =>
        id < traits.Length ? traits[id] : default;

    /// <summary>
    /// 面剔除唯一判定（与 MC 口径一致）：
    /// 自身空气/未知 → 不渲染；邻居不透明 → 被遮挡；
    /// 不透明/切图 → 渲染；半透明/液体 → 仅同 ID 邻居剔内面。
    /// </summary>
    private static bool FaceVisible(ushort current, ushort neighbor, ReadOnlySpan<VoxelTraits> traits)
    {
        if (current == 0) return false;

        var self = TraitsOf(current, traits).CullingClass;
        if (self == VoxelRenderClass.Air) return false;
        if (TraitsOf(neighbor, traits).CullingClass == VoxelRenderClass.Opaque) return false;

        return self switch
        {
            VoxelRenderClass.Opaque => true,
            VoxelRenderClass.Cutout => true,
            VoxelRenderClass.Translucent => neighbor != current,
            VoxelRenderClass.Liquid => neighbor != current,
            _ => false,
        };
    }

    private static bool Liquid(ushort id, ReadOnlySpan<VoxelTraits> traits) => TraitsOf(id, traits).Liquid;

    /// <summary>face: 0=顶、1=侧、2=底。</summary>
    private static int TileFor(ushort id, int face, ReadOnlySpan<VoxelTraits> traits)
    {
        var def = TraitsOf(id, traits);
        return face switch
        {
            0 => def.TopTile,
            2 => def.BottomTile,
            _ => def.SideTile,
        };
    }

    private static int ComputeAo(
        ReadOnlySpan<ushort> padded, ReadOnlySpan<VoxelTraits> traits, int[] neighbor,
        int u, int v, int du, int dv, int dim)
    {
        int side1 = SolidAt(padded, traits, neighbor, u, du, dim);
        int side2 = SolidAt(padded, traits, neighbor, v, dv, dim);

        int[] diagonal = { neighbor[0], neighbor[1], neighbor[2] };
        diagonal[u] += du;
        diagonal[v] += dv;
        int corner = SolidAt(padded, traits, diagonal, 0, 0, dim);

        if (side1 != 0 && side2 != 0) return 0;
        return 3 - side1 - side2 - corner;
    }

    private static int SolidAt(
        ReadOnlySpan<ushort> padded, ReadOnlySpan<VoxelTraits> traits, int[] p, int axis, int delta, int dim)
    {
        int[] q = { p[0], p[1], p[2] };
        q[axis] += delta;
        if (q[0] < 0 || q[1] < 0 || q[2] < 0 || q[0] >= dim || q[1] >= dim || q[2] >= dim)
            return 0;
        return TraitsOf(padded[Index(q, dim)], traits).Opaque ? 1 : 0;
    }

    private static int Index(int[] p, int dim) => p[0] + dim * (p[1] + dim * p[2]);
}
