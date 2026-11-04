
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
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
/// <para><see cref="RenderClass"/> 缺省为 <see cref="VoxelRenderClass.Auto"/>；6 参构造签名保持不变（二进制兼容），
/// 旧行为：不透明→不透明、液体→液体、其余→切图（现在会出面，此前被误判为不出面）。</para>
/// </summary>
public readonly record struct VoxelTraits(
    bool Opaque,
    bool Liquid,
    int TopTile,
    int SideTile,
    int BottomTile,
    int LightEmission)
{
    /// <summary>剔除类别；旧 6 参构造时为 Auto（按 <c>Opaque/Liquid</c> 推导）。</summary>
    public VoxelRenderClass RenderClass { get; init; } = VoxelRenderClass.Auto;

    /// <summary>显式指定剔除类别（7 参便利构造，保留原 6 参构造的 IL 签名）。</summary>
    public VoxelTraits(
        bool opaque, bool liquid, int topTile, int sideTile, int bottomTile, int lightEmission,
        VoxelRenderClass renderClass)
        : this(opaque, liquid, topTile, sideTile, bottomTile, lightEmission)
    {
        RenderClass = renderClass;
    }

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
/// <para>实现要点：进入三轴循环前把 traits 预折叠为 <c>cullClass</c> / <c>isOpaque</c> 两张
/// per-voxel 扁平表，内层循环只做字节数组随机访问与纯算术；面内不再分配任何托管对象。</para>
/// </summary>
public static class VoxelMesher
{
    /// <summary>光照等级上限（与方块光照传播算法同一口径）。</summary>
    public const int MaxLightLevel = 15;

    private const int NegativeFlag = 0x10000;

    /// <summary>相邻 greedy 矩形尺寸不同会产生 T-junction 裂缝；向外扩 0.02 个世界单位覆盖裂缝。</summary>
    private const float SeamEpsilon = 0.02f;

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

        // ---- 预计算：每格剔除类别（供 FaceVisible 用）与不透明标记（供 AO 用）----
        // 内层循环不再访问 traits，避免重复查表 + CullingClass 三元推导。
        var cullClass = new byte[volume];
        var isOpaque = new byte[volume];
        int traitCount = traits.Length;
        for (int idx = 0; idx < volume; idx++)
        {
            var t = TraitsOf(paddedBlocks[idx], traits);
            cullClass[idx] = (byte)t.CullingClass;
            isOpaque[idx] = (byte)(t.Opaque ? 1 : 0);
        }

        var opaqueVertices = new List<VoxelVertex>(chunkSize * chunkSize * 2);
        var opaqueIndices = new List<uint>(chunkSize * chunkSize * 3);
        var waterVertices = new List<VoxelVertex>(chunkSize * chunkSize);
        var waterIndices = new List<uint>(chunkSize * chunkSize * 3);

        int[] stride = { 1, dim, dim * dim };
        int[] origin = { originX, originY, originZ };
        int planeCells = chunkSize * chunkSize;

        for (int d = 0; d < 3; d++)
        {
            int u = (d + 1) % 3;
            int v = (d + 2) % 3;
            int sD = stride[d], sU = stride[u], sV = stride[v];
            int od = origin[d], ou = origin[u], ov = origin[v];

            var mask = new int[planeCells];
            var used = new bool[planeCells];

            for (int plane = 0; plane <= chunkSize; plane++)
            {
                Array.Clear(mask, 0, planeCells);
                Array.Clear(used, 0, planeCells);

                int planeOffset = plane * sD;
                bool isFirst = plane == 0;
                bool isLast = plane == chunkSize;

                // ---- 1) 填 mask：走增量索引，内层只有加法 ----
                for (int j = 0; j < chunkSize; j++)
                {
                    int rowBase = planeOffset + (j + 1) * sV;
                    int cellRow = j * chunkSize;
                    int idxA = rowBase + sU; // i = 0 → (i+1) = 1
                    for (int i = 0; i < chunkSize; i++, idxA += sU)
                    {
                        int idxB = idxA + sD;
                        ushort a = paddedBlocks[idxA];
                        ushort b = paddedBlocks[idxB];
                        int cell = cellRow + i;

                        if (isFirst)
                        {
                            // a 属邻块，b 是本块 local 0：只允许 b 的 -d 面。
                            if (FaceVisible(cullClass[idxB], b, cullClass[idxA], a))
                                mask[cell] = b | NegativeFlag;
                        }
                        else if (isLast)
                        {
                            // a 是本块 local chunkSize-1，b 属邻块：只允许 a 的 +d 面。
                            if (FaceVisible(cullClass[idxA], a, cullClass[idxB], b))
                                mask[cell] = a;
                        }
                        else if (FaceVisible(cullClass[idxA], a, cullClass[idxB], b))
                        {
                            mask[cell] = a;
                        }
                        else if (FaceVisible(cullClass[idxB], b, cullClass[idxA], a))
                        {
                            mask[cell] = b | NegativeFlag;
                        }
                    }
                }

                // ---- 2) greedy 合并 + 出顶点 ----
                for (int j = 0; j < chunkSize; j++)
                {
                    int cellRow = j * chunkSize;
                    for (int i = 0; i < chunkSize; i++)
                    {
                        int cell = cellRow + i;
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
                                int idx = row + k;
                                if (mask[idx] != value || used[idx]) { rowMatches = false; break; }
                            }
                            if (!rowMatches) break;
                            height++;
                        }

                        for (int hh = 0; hh < height; hh++)
                        {
                            int rowStart = (j + hh) * chunkSize + i;
                            for (int ww = 0; ww < width; ww++)
                                used[rowStart + ww] = true;
                        }

                        bool negative = (value & NegativeFlag) != 0;
                        ushort id = (ushort)(value & 0xFFFF);

                        // tile / liquid 每面只查一次 traits（id 通常 < traitCount；越界回退 default）。
                        bool liquid = false;
                        float tile = 0f;
                        if (id < traitCount)
                        {
                            var t = traits[id];
                            liquid = t.Liquid;
                            int faceKind = d == 1 ? (negative ? 2 : 0) : 1;
                            tile = faceKind == 0 ? t.TopTile
                                 : faceKind == 2 ? t.BottomTile
                                 : t.SideTile;
                        }

                        // 面外侧（neighbor）位置偏移：AO 与光照采样共用。
                        int nD = negative ? plane : plane + 1;
                        int nU = i + 1;
                        int nV = j + 1;
                        int nOffset = nD * sD + nU * sU + nV * sV;

                        float lightLevel = paddedLight[nOffset] / (float)MaxLightLevel;

                        float nX = d == 0 ? (negative ? -1f : 1f) : 0f;
                        float nY = d == 1 ? (negative ? -1f : 1f) : 0f;
                        float nZ = d == 2 ? (negative ? -1f : 1f) : 0f;

                        int wD = od + plane;
                        float eU0 = (ou + i) - SeamEpsilon;
                        float eU1 = (ou + i + width) + SeamEpsilon;
                        float eV0 = (ov + j) - SeamEpsilon;
                        float eV1 = (ov + j + height) + SeamEpsilon;

                        // 四角按周长顺序 (cu,cv) = (0,0),(1,0),(1,1),(0,1)。
                        float c0x = 0, c0y = 0, c0z = 0;
                        SetComp(ref c0x, ref c0y, ref c0z, d, wD);
                        SetComp(ref c0x, ref c0y, ref c0z, u, eU0);
                        SetComp(ref c0x, ref c0y, ref c0z, v, eV0);

                        float c1x = 0, c1y = 0, c1z = 0;
                        SetComp(ref c1x, ref c1y, ref c1z, d, wD);
                        SetComp(ref c1x, ref c1y, ref c1z, u, eU1);
                        SetComp(ref c1x, ref c1y, ref c1z, v, eV0);

                        float c2x = 0, c2y = 0, c2z = 0;
                        SetComp(ref c2x, ref c2y, ref c2z, d, wD);
                        SetComp(ref c2x, ref c2y, ref c2z, u, eU1);
                        SetComp(ref c2x, ref c2y, ref c2z, v, eV1);

                        float c3x = 0, c3y = 0, c3z = 0;
                        SetComp(ref c3x, ref c3y, ref c3z, d, wD);
                        SetComp(ref c3x, ref c3y, ref c3z, u, eU0);
                        SetComp(ref c3x, ref c3y, ref c3z, v, eV1);

                        // AO：每面 4 次标量采样，无数组分配。
                        float ao0 = ComputeAo(isOpaque, nOffset, -1, -1, sU, sV) / 3f;
                        float ao1 = ComputeAo(isOpaque, nOffset, +1, -1, sU, sV) / 3f;
                        float ao2 = ComputeAo(isOpaque, nOffset, +1, +1, sU, sV) / 3f;
                        float ao3 = ComputeAo(isOpaque, nOffset, -1, +1, sU, sV) / 3f;

                        var verts = liquid ? waterVertices : opaqueVertices;
                        var inds = liquid ? waterIndices : opaqueIndices;
                        uint baseIndex = (uint)verts.Count;

                        verts.Add(new VoxelVertex(c0x, c0y, c0z, nX, nY, nZ, 0f, 0f, tile, lightLevel, ao0));
                        verts.Add(new VoxelVertex(c1x, c1y, c1z, nX, nY, nZ, width, 0f, tile, lightLevel, ao1));
                        verts.Add(new VoxelVertex(c2x, c2y, c2z, nX, nY, nZ, width, height, tile, lightLevel, ao2));
                        verts.Add(new VoxelVertex(c3x, c3y, c3z, nX, nY, nZ, 0f, height, tile, lightLevel, ao3));

                        inds.Add(baseIndex + 0);
                        inds.Add(baseIndex + 1);
                        inds.Add(baseIndex + 2);
                        inds.Add(baseIndex + 0);
                        inds.Add(baseIndex + 2);
                        inds.Add(baseIndex + 3);
                    }
                }
            }
        }

        return new VoxelMeshData(
            version,
            opaqueVertices.ToArray(), opaqueIndices.ToArray(),
            waterVertices.ToArray(), waterIndices.ToArray());
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static VoxelTraits TraitsOf(ushort id, ReadOnlySpan<VoxelTraits> traits) =>
        id < traits.Length ? traits[id] : default;

    /// <summary>
    /// 面剔除唯一判定（与 MC 口径一致）：
    /// 自身空气/未知 → 不渲染；邻居不透明 → 被遮挡；
    /// 不透明/切图 → 渲染；半透明/液体 → 仅同 ID 邻居剔内面。
    /// <para>分类已由调用方预计算为 <c>cullClass</c>，此处只做字节比较与 id 等值比较。</para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static bool FaceVisible(byte selfClass, ushort selfId, byte nbrClass, ushort nbrId)
    {
        if (selfId == 0) return false;
        if (selfClass == (byte)VoxelRenderClass.Air) return false;
        if (nbrClass == (byte)VoxelRenderClass.Opaque) return false;

        return selfClass switch
        {
            (byte)VoxelRenderClass.Opaque => true,
            (byte)VoxelRenderClass.Cutout => true,
            (byte)VoxelRenderClass.Translucent => nbrId != selfId,
            (byte)VoxelRenderClass.Liquid => nbrId != selfId,
            _ => false,
        };
    }

    /// <summary>
    /// 单角 AO 采样：<paramref name="nOffset"/> 是面外侧格在 padded 中的线性索引；
    /// <paramref name="du"/>/<paramref name="dv"/> 是该角沿 u/v 轴的 ±1；
    /// <paramref name="sU"/>/<paramref name="sV"/> 是 u/v 轴在 padded 中的 stride。
    /// <para>padded 保证了 neighbor ± 1 仍在 [0, dim-1] 内，因此无边界检查。</para>
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int ComputeAo(
        ReadOnlySpan<byte> isOpaque, int nOffset,
        int du, int dv, int sU, int sV)
    {
        int side1 = isOpaque[nOffset + du * sU];
        int side2 = isOpaque[nOffset + dv * sV];
        int corner = isOpaque[nOffset + du * sU + dv * sV];

        if (side1 != 0 && side2 != 0) return 0;
        return 3 - side1 - side2 - corner;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SetComp(ref float x, ref float y, ref float z, int axis, float value)
    {
        if (axis == 0) x = value;
        else if (axis == 1) y = value;
        else z = value;
    }
}