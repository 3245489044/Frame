namespace MathematicalAlgorithms.Noise;

/// <summary>
/// 地形剖面常量（声明面 <c>engine.voxel.terrain</c> 的米制值）。
/// <para>这里只存<b>数据</b>，换算到 cm 世界由 <see cref="TerrainHeightModel"/> 统一完成；
/// CPU 生成器与 GPU 常量注入都消费同一份换算结果，避免两套口径漂移。</para>
/// </summary>
public readonly record struct TerrainProfile(
    int SeaLevel,
    float HeightScale,
    float HeightOffset,
    int SoilDepth);
