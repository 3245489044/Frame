using MathematicalAlgorithms.Culling;
using TConfig.GameMath;

namespace MathematicalAlgorithms.Spatial;

/// <summary>
/// 分块可见性规划：候选域枚举 + 视锥剔除 + 距离上限 + 近→远排序 + 限额截断 + 保守渲染域。
/// <para>纯计算：输入只有眼点/视锥/分块参数（可选高度提示），输出 <see cref="ChunkStreamSnapshot"/>;
/// 不读取世界内容、不持有版本或脏块状态。</para>
/// <para>垂直候选域不再只枚举眼点所在区块的邻域：同时并入 <c>heightSpan</c> 给出的内容高度带，
/// 从结构上保证「脚下地表」进入候选集（1 cm 世界里眼点比地表高约 1.6 m ≈ 10 个 16 cm 区块）。</para>
/// </summary>
public static class ChunkVisibility
{
    /// <summary>水平 footprint → 可能非空气的高度范围（cm）。为 null 时退化为眼点邻域枚举。</summary>
    public delegate HeightSpan HeightSpanQuery(float minX, float minZ, float maxX, float maxZ);

    public static ChunkStreamSnapshot Plan(
        in Vec3 eye, in Frustum frustum, in ChunkStreamOptions options, int chunkSize,
        HeightSpanQuery? heightSpan = null)
    {
        int horizontal = Math.Max(0, options.HorizontalChunks);
        int vertical = Math.Max(1, options.VerticalChunks);
        var eyeChunk = ChunkCoord.Of(eye, chunkSize);

        int maxVisible = options.MaxVisibleChunks > 0 ? options.MaxVisibleChunks : int.MaxValue;
        var visible = new List<(ChunkCoord Coord, float Distance)>();
        int candidateCount = 0;
        Aabb streamBounds = default;
        bool hasBounds = false;

        for (int cx = eyeChunk.X - horizontal; cx <= eyeChunk.X + horizontal; cx++)
        for (int cz = eyeChunk.Z - horizontal; cz <= eyeChunk.Z + horizontal; cz++)
        {
            int minX = ChunkSpace.ChunkOrigin(cx, chunkSize);
            int minZ = ChunkSpace.ChunkOrigin(cz, chunkSize);
            int maxX = minX + chunkSize - 1;
            int maxZ = minZ + chunkSize - 1;

            int cy0, cy1;
            if (heightSpan != null)
            {
                // 有内容高度带 ⇒ 只枚举可能非空气的分块（空列直接跳过）。
                // 不用眼点邻域兜底：眼点周围全是空气，按距离排序会挤掉脚下的地表（tris=0 的来源）。
                var span = heightSpan(minX, minZ, maxX, maxZ);
                if (span.IsEmpty)
                    continue;
                cy0 = ChunkSpace.ChunkIndex(span.MinY, chunkSize);
                cy1 = ChunkSpace.ChunkIndex(span.MaxY, chunkSize);
            }
            else
            {
                cy0 = eyeChunk.Y - vertical;
                cy1 = eyeChunk.Y + vertical;
            }

            for (int cy = cy0; cy <= cy1; cy++)
            {
                candidateCount++;
                var chunkBounds = new ChunkCoord(cx, cy, cz).Bounds(chunkSize);
                streamBounds = hasBounds ? Union(streamBounds, chunkBounds) : chunkBounds;
                hasBounds = true;

                if (!frustum.Intersects(chunkBounds))
                    continue;

                var center = new Vec3(
                    (chunkBounds.MinX + chunkBounds.MaxX) * 0.5f,
                    (chunkBounds.MinY + chunkBounds.MaxY) * 0.5f,
                    (chunkBounds.MinZ + chunkBounds.MaxZ) * 0.5f);

                float distance = frustum.DistanceTo(center);
                if (distance > options.MaxDistance)
                    continue;

                visible.Add((new ChunkCoord(cx, cy, cz), distance));
            }
        }

        // 近→远：渲染与流式都按此顺序消费，保证先看到近处数据。
        visible.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));

        int take = Math.Min(visible.Count, maxVisible);
        var visibleChunks = new List<ChunkCoord>(take);
        for (int i = 0; i < take; i++)
            visibleChunks.Add(visible[i].Coord);

        // 保守渲染域 = 候选范围并集（不因朝向裁掉射线可能进入的 chunk）。
        return new ChunkStreamSnapshot(visibleChunks, streamBounds, candidateCount);
    }

    private static Aabb Union(in Aabb a, in Aabb b) => new(
        Math.Min(a.MinX, b.MinX), Math.Min(a.MinY, b.MinY), Math.Min(a.MinZ, b.MinZ),
        Math.Max(a.MaxX, b.MaxX), Math.Max(a.MaxY, b.MaxY), Math.Max(a.MaxZ, b.MaxZ));
}
