namespace MathematicalAlgorithms.Fitting;

/// <summary>曲线形状分类（仅描述拟合器形态，不含领域语义）。</summary>
public enum FitKind
{
    /// <summary>自定义基函数线性组合。</summary>
    Basis,

    /// <summary><c>a*x + b</c>。</summary>
    Linear,

    /// <summary><c>a*x^3 + b</c>。</summary>
    CubicOffset,

    /// <summary><c>a*x^b</c>。</summary>
    PowerLaw,

    /// <summary><c>a*x/(b+x) + c</c>。</summary>
    RationalSaturation,

    /// <summary><c>a*(1 - exp(-b*x)) + c</c>。</summary>
    ExponentialSaturation,

    /// <summary><c>a*φ(x)</c>，单参数标定。</summary>
    Calibration,
}

/// <summary>有序系数项；顺序由拟合器声明，签名侧按名称再排序。</summary>
public readonly record struct FitCoefficient(string Name, double Value);

/// <summary>
/// 拟合曲线：纯函数求值，不负责适用域判定、不夹取、不外推。
/// </summary>
public interface IFitCurve
{
    FitKind Kind { get; }

    /// <summary>人类可读表达式，仅作文档；求值不走字符串解析。</summary>
    string Expression { get; }

    /// <summary>系数（按拟合器声明顺序）。</summary>
    IReadOnlyList<FitCoefficient> Coefficients { get; }

    /// <summary>在 x 处求值；域外行为由调用方决定。</summary>
    double Evaluate(double x);
}
