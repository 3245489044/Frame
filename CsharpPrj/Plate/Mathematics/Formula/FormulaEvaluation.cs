namespace MathematicalAlgorithms.Formula;

/// <summary>
/// 公式求值三态：命中、越域、签名不一致。
/// <para>禁止把越域当 0、禁止夹取到边界、禁止把签名不一致当命中。</para>
/// </summary>
public abstract record FormulaEvaluation
{
    private FormulaEvaluation()
    {
    }

    /// <summary>命中：返回值与公式签名。</summary>
    public sealed record Hit(double Value, long Signature) : FormulaEvaluation;

    /// <summary>越域：返回原因与被违反的输入名。</summary>
    public sealed record OutOfDomain(string Reason, string ViolatedInput) : FormulaEvaluation;

    /// <summary>签名不一致：公式版本/系数/域与期望不符。</summary>
    public sealed record Stale(long ExpectedSignature, long ActualSignature) : FormulaEvaluation;
}
