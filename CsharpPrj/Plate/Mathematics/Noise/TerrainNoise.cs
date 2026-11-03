namespace MathematicalAlgorithms.Noise;

/// <summary>
/// 地形高度噪声（低频值噪声双线性插值 + 两个小尺度细节）。
/// <para>算法与常量权重一并下沉：<c>0.62·value(16) + 0.28·value(8) + 0.10·value(4)</c>。</para>
/// <para>不做平滑步进（保持线性插值），与迁移前实现逐值一致；只依赖 BCL，确定性。</para>
/// </summary>
public static class TerrainNoise
{
    /// <summary>归一化地形高度（约 -1..1），供上层按 terrain 常量换算为高度/高度带。</summary>
    public static float ComputeHeight(int x, int z, int seed)
        => ComputeHeight(x, z, seed, unitsPerMeter: 1);

    /// <summary>
    /// 归一化地形高度（单位感知）：<paramref name="unitsPerMeter"/> 为坐标单位/米。
    /// <para>传入 100 时，x/z 为厘米坐标，波长同步放大到 1600/800/400 cm。</para>
    /// <para>CPU 与 GPU 必须共用该公式（GPU 用 cm 波长），避免负坐标取整分叉。</para>
    /// </summary>
    public static float ComputeHeight(int x, int z, int seed, int unitsPerMeter)
    {
        if (unitsPerMeter <= 0)
            throw new System.ArgumentOutOfRangeException(nameof(unitsPerMeter));

        return 0.62f * ValueNoise(x, z, 16 * unitsPerMeter, seed) +
               0.28f * ValueNoise(x, z, 8 * unitsPerMeter, seed) +
               0.10f * ValueNoise(x, z, 4 * unitsPerMeter, seed);
    }

    public static float ValueNoise(int x, int z, int cell, int seed)
    {
        int x0 = FloorDiv(x, cell);
        int z0 = FloorDiv(z, cell);
        float u = (x - x0 * cell) / (float)cell;
        float v = (z - z0 * cell) / (float)cell;

        float h00 = HashToUnit(x0, z0, seed);
        float h10 = HashToUnit(x0 + 1, z0, seed);
        float h01 = HashToUnit(x0, z0 + 1, seed);
        float h11 = HashToUnit(x0 + 1, z0 + 1, seed);

        float a = h00 + (h10 - h00) * u;
        float b = h01 + (h11 - h01) * u;
        return a + (b - a) * v;
    }

    private static int FloorDiv(int value, int divisor) =>
        (int)System.MathF.Floor(value / (float)divisor);

    private static float HashToUnit(int x, int z, int seed) =>
        NoisePrimitives.HashToFloat(NoisePrimitives.Hash2D(x, z, seed)) * 2f - 1f;
}
