namespace MathematicalAlgorithms.Noise;

/// <summary>
/// 地形高度公式的唯一实现：<c>HeightOffset + bias + noise * HeightScale * amplitude</c>。
/// <para>CPU 生成器（<see cref="TerrainHeightModel.HeightCm"/>）与 GPU 高度契约（<see cref="WorldHeightContract"/>）
/// 共用本方法，避免出现第二套公式或浮点运算顺序分叉。</para>
/// </summary>
public static class HeightFormula
{
    public static float Evaluate(
        in TerrainConstants constants,
        IReadOnlyList<BiomeClimate> biomes,
        in BiomeSample sample,
        float noise)
    {
        float weight = sample.PrimaryWeight;
        float amplitude = biomes[sample.Primary].HeightAmplitude * weight
            + biomes[sample.Secondary].HeightAmplitude * (1f - weight);
        float bias = biomes[sample.Primary].HeightBiasCm * weight
            + biomes[sample.Secondary].HeightBiasCm * (1f - weight);

        return constants.HeightOffsetCm + bias + noise * constants.HeightScaleCm * amplitude;
    }
}
