namespace MathematicalAlgorithms.Formula;

/// <summary>公式种类（与设计契约一致）。</summary>
public enum FormulaKind
{
    Constant,
    ClosedForm,
    Calibrated,
    Piecewise,
}

/// <summary>公式生命周期状态。</summary>
public enum FormulaStatus
{
    Provisional,
    Active,
    Retired,
}

/// <summary>
/// 求值形状：替代字符串表达式解析器。
/// <para>闭式公式（如驻留瓦片数）必须落成显式形状，避免运行时解析表达式。</para>
/// </summary>
public enum FormulaShape
{
    Constant,
    Linear,
    CubicOffset,
    PowerLaw,
    RationalSaturation,
    ResidentTiles,
    QuotaMin,
    Piecewise,
}

/// <summary>越域守卫行为；当前只允许拒绝。</summary>
public enum FormulaGuard
{
    Refuse,
}
