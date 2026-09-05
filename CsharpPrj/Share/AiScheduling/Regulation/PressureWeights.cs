namespace Share.AiScheduling.Regulation;

/// <summary>三维压力权重（Context / InfoGain / Complexity），用于加权 Score。</summary>
public sealed class PressureWeights
{
    public double Context { get; set; } = 0.5;

    public double InfoGain { get; set; } = 0.3;

    public double Complexity { get; set; } = 0.2;

    /// <summary>累计无进展步数权重（复杂度压力的深度项）。默认 0（不启用，保持行为不变）。</summary>
    public double ComplexitySteps { get; set; } = 0;
}
