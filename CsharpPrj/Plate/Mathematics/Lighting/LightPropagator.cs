using System;
using System.Collections.Generic;

namespace MathematicalAlgorithms.Lighting;

/// <summary>
/// 方块光照传播（天空光按列截断 + 方块光 6 邻接 BFS）。
/// <para>输入只有「不透明」与「自发光」两个正交数组，不含方块身份/游戏语义；输出每格光强。</para>
/// <para>在 (chunkSize+2)³ 的 halo 上求解，因此跨块边界连续；结果确定性（BFS 顺序固定）。</para>
/// </summary>
public static class LightPropagator
{
    public const byte MaxLight = 15;

    /// <param name="dim">立方体边长（= chunkSize + 2）。</param>
    /// <param name="opaque">每格是否不透明（非 0 即不透明），长度 ≥ dim³。</param>
    /// <param name="emission">每格自发光强度 0..15，长度 ≥ dim³。</param>
    /// <param name="light">输出光强，长度 ≥ dim³。</param>
    public static void Solve(int dim, ReadOnlySpan<byte> opaque, ReadOnlySpan<byte> emission, Span<byte> light)
    {
        if (dim <= 0) throw new ArgumentOutOfRangeException(nameof(dim));
        int volume = dim * dim * dim;
        if (opaque.Length < volume)
            throw new ArgumentException("opaque 数组不足 dim³", nameof(opaque));
        if (emission.Length < volume)
            throw new ArgumentException("emission 数组不足 dim³", nameof(emission));
        if (light.Length < volume)
            throw new ArgumentException("light 输出不足 dim³", nameof(light));

        light.Slice(0, volume).Clear();

        // 天光：从顶部向下，遇到不透明方块后该列归零。
        for (int z = 0; z < dim; z++)
        for (int x = 0; x < dim; x++)
        {
            byte sky = MaxLight;
            for (int y = dim - 1; y >= 0; y--)
            {
                int index = x + dim * (y + dim * z);
                if (opaque[index] != 0)
                    sky = 0;
                light[index] = sky;
            }
        }

        // 方块光：以发光格为源，向非不透明邻接扩散，逐级衰减 1。
        var queue = new Queue<int>();
        for (int z = 0; z < dim; z++)
        for (int y = 0; y < dim; y++)
        for (int x = 0; x < dim; x++)
        {
            int index = x + dim * (y + dim * z);
            byte value = Math.Min(emission[index], MaxLight);
            if (value <= light[index]) continue;
            light[index] = value;
            queue.Enqueue(index);
        }

        while (queue.Count > 0)
        {
            int index = queue.Dequeue();
            int level = light[index];
            if (level <= 1) continue;

            int x = index % dim;
            int y = (index / dim) % dim;
            int z = index / (dim * dim);

            for (int axis = 0; axis < 3; axis++)
            for (int sign = -1; sign <= 1; sign += 2)
            {
                int nx = x, ny = y, nz = z;
                if (axis == 0) nx += sign;
                else if (axis == 1) ny += sign;
                else nz += sign;
                if (nx < 0 || nx >= dim || ny < 0 || ny >= dim || nz < 0 || nz >= dim) continue;

                int nIndex = nx + dim * (ny + dim * nz);
                if (opaque[nIndex] != 0) continue;

                byte next = (byte)(level - 1);
                if (next <= light[nIndex]) continue;
                light[nIndex] = next;
                queue.Enqueue(nIndex);
            }
        }
    }
}
