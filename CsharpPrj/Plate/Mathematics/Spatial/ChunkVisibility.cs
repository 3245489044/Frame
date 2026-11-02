using System;
using System.Collections.Generic;
using MathematicalAlgorithms.Culling;
using TConfig.GameMath;

namespace MathematicalAlgorithms.Spatial;

/// <summary>
/// 分块可见性规划：候选域枚举 + 视锥剔除 + 距离上限 + 近→远排序 + 限额截断 + 保守渲染域。
/// <para>纯计算：输入只有眼点/视锥/分块参数，输出 <see cref="ChunkStreamSnapshot"/>；
/// 不读取世界内容、不持有版本或脏块状态。</para>
/// <para>自 Engine <c>VirtualMatrix.QueryVisibility</c> 提取：候选集与朝向无关，可见集按距离升序，
/// 逐值保持原实现（chunk 键与包围盒口径不变）。</para>
/// </summary>
public static class ChunkVisibility
{
    public static ChunkStreamSnapshot Plan(
        in Vec3 eye, in Frustum frustum, in ChunkStreamOptions options, int chunkSize)
    {
        int horizontal = Math.Max(0, options.HorizontalChunks);
        int vertical = Math.Max(1, options.VerticalChunks);
        var eyeChunk = ChunkCoord.Of(eye, chunkSize);

        var candidates = new List<ChunkCoord>((2 * horizontal + 1) * (2 * horizontal + 1) * (2 * vertical + 1));
        var visible = new List<(ChunkCoord Coord, float Distance)>();

        for (int cx = eyeChunk.X - horizontal; cx <= eyeChunk.X + horizontal; cx++)
        for (int cy = eyeChunk.Y - vertical; cy <= eyeChunk.Y + vertical; cy++)
        for (int cz = eyeChunk.Z - horizontal; cz <= eyeChunk.Z + horizontal; cz++)
        {
            var chunk = new ChunkCoord(cx, cy, cz);
            candidates.Add(chunk);

            var chunkBounds = chunk.Bounds(chunkSize);
            if (!frustum.Intersects(chunkBounds))
                continue;

            var center = new Vec3(
                (chunkBounds.MinX + chunkBounds.MaxX) * 0.5f,
                (chunkBounds.MinY + chunkBounds.MaxY) * 0.5f,
                (chunkBounds.MinZ + chunkBounds.MaxZ) * 0.5f);

            float distance = frustum.DistanceTo(center);
            if (distance > options.MaxDistance)
                continue;

            visible.Add((chunk, distance));
        }

        // 近→远：渲染与流式都按此顺序消费，保证先看到近处数据。
        visible.Sort(static (a, b) => a.Distance.CompareTo(b.Distance));

        int maxVisible = options.MaxVisibleChunks > 0 ? options.MaxVisibleChunks : visible.Count;
        var visibleChunks = new List<ChunkCoord>(Math.Min(visible.Count, maxVisible));
        for (int i = 0; i < visible.Count && i < maxVisible; i++)
            visibleChunks.Add(visible[i].Coord);

        // 保守渲染域 = 候选范围并集（不因朝向裁掉射线可能进入的 chunk）。
        Aabb streamBounds = candidates.Count > 0 ? candidates[0].Bounds(chunkSize) : default;
        foreach (var chunk in candidates)
            streamBounds = Union(streamBounds, chunk.Bounds(chunkSize));

        return new ChunkStreamSnapshot(visibleChunks, streamBounds, candidates.Count);
    }

    private static Aabb Union(in Aabb a, in Aabb b) => new(
        Math.Min(a.MinX, b.MinX), Math.Min(a.MinY, b.MinY), Math.Min(a.MinZ, b.MinZ),
        Math.Max(a.MaxX, b.MaxX), Math.Max(a.MaxY, b.MaxY), Math.Max(a.MaxZ, b.MaxZ));
}
