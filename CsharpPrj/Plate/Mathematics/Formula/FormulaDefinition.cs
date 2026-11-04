using MathematicalAlgorithms.Fitting;

namespace MathematicalAlgorithms.Formula;

/// <summary>
/// 公式定义：契约字段 + 显式求值形状。
/// <para><see cref="FormulaShape"/> 取代字符串表达式解析器；<see cref="Normalize"/> 会校验并重算签名。</para>
/// </summary>
public sealed record FormulaDefinition
{
    public required string Id { get; init; }

    public required int Version { get; init; }

    public required FormulaKind Kind { get; init; }

    public required FormulaShape Shape { get; init; }

    public required string Target { get; init; }

    public required string Unit { get; init; }

    public IReadOnlyList<FormulaInputSpec> Inputs { get; init; } = Array.Empty<FormulaInputSpec>();

    public string Expression { get; init; } = string.Empty;

    public IReadOnlyList<FormulaCoefficient> Coefficients { get; init; } = Array.Empty<FormulaCoefficient>();

    public IReadOnlyList<FormulaSegment> Segments { get; init; } = Array.Empty<FormulaSegment>();

    public FormulaFitRecord Fit { get; init; } = FormulaFitRecord.Empty;

    public FitResiduals Residual { get; init; } = new(0, 0, 0, null);

    public FormulaDomain Domain { get; init; } = FormulaDomain.Empty;

    public FormulaStatus Status { get; init; } = FormulaStatus.Provisional;

    public long Signature { get; init; }

    /// <summary>校验结构完整性；失败抛 <see cref="ArgumentException"/>（加载期 fail-fast）。</summary>
    public FormulaDefinition Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new ArgumentException("公式 id 不能为空。", nameof(Id));

        if (Version <= 0)
            throw new ArgumentException($"公式 {Id} 的 version 必须为正。", nameof(Version));

        if (string.IsNullOrWhiteSpace(Target))
            throw new ArgumentException($"公式 {Id} 的 target 不能为空。", nameof(Target));

        if (string.IsNullOrWhiteSpace(Unit))
            throw new ArgumentException($"公式 {Id} 的 unit 不能为空。", nameof(Unit));

        if (Inputs is null)
            throw new ArgumentException($"公式 {Id} 的 inputs 不能为空。", nameof(Inputs));

        if (Coefficients is null)
            throw new ArgumentException($"公式 {Id} 的 coefficients 不能为空。", nameof(Coefficients));

        if (Segments is null)
            throw new ArgumentException($"公式 {Id} 的 segments 不能为空。", nameof(Segments));

        if (Fit is null)
            throw new ArgumentException($"公式 {Id} 的 fit 不能为空。", nameof(Fit));

        if (Domain is null)
            throw new ArgumentException($"公式 {Id} 的 domain 不能为空。", nameof(Domain));

        var inputNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (FormulaInputSpec input in Inputs)
        {
            if (input is null || string.IsNullOrWhiteSpace(input.Name))
                throw new ArgumentException($"公式 {Id} 含空输入名。", nameof(Inputs));

            if (input.Domain is null)
                throw new ArgumentException($"公式 {Id} 的输入 {input.Name} 缺少适用域。", nameof(Inputs));

            if (!inputNames.Add(input.Name))
                throw new ArgumentException($"公式 {Id} 的输入名 {input.Name} 重复。", nameof(Inputs));
        }

        var coefficientNames = new HashSet<string>(StringComparer.Ordinal);
        foreach (FormulaCoefficient coefficient in Coefficients)
        {
            if (coefficient is null || string.IsNullOrWhiteSpace(coefficient.Name))
                throw new ArgumentException($"公式 {Id} 含空系数名。", nameof(Coefficients));

            if (!double.IsFinite(coefficient.Value))
                throw new ArgumentException($"公式 {Id} 的系数 {coefficient.Name} 不是有限值。", nameof(Coefficients));

            if (!coefficientNames.Add(coefficient.Name))
                throw new ArgumentException($"公式 {Id} 的系数名 {coefficient.Name} 重复。", nameof(Coefficients));
        }

        foreach (FormulaSegment segment in Segments)
        {
            if (segment is null)
                throw new ArgumentException($"公式 {Id} 含空分段。", nameof(Segments));

            if (!double.IsFinite(segment.MinX) || !double.IsFinite(segment.MaxX)
                || !double.IsFinite(segment.Slope) || !double.IsFinite(segment.Intercept))
            {
                throw new ArgumentException($"公式 {Id} 的分段含非有限值。", nameof(Segments));
            }

            if (segment.MinX > segment.MaxX)
                throw new ArgumentException($"公式 {Id} 的分段区间反向。", nameof(Segments));
        }

        ValidateShape();
        ValidateActiveState();
        return this;
    }

    /// <summary>校验 + 排序输入/系数/分段/来源 + 重算签名。</summary>
    public FormulaDefinition Normalize()
    {
        Validate();

        FormulaDefinition normalized = this with
        {
            Inputs = Inputs.OrderBy(static i => i.Name, StringComparer.Ordinal).ToArray(),
            Coefficients = Coefficients.OrderBy(static c => c.Name, StringComparer.Ordinal).ToArray(),
            Segments = Segments
                .OrderBy(static s => s.MinX)
                .ThenBy(static s => s.MaxX)
                .ToArray(),
            Fit = Fit.Normalize(),
            Domain = Domain.Normalize(),
        };

        return normalized with { Signature = FormulaSignature.Compute(normalized) };
    }

    public FormulaEvaluation Evaluate(
        IReadOnlyDictionary<string, double> inputs,
        long expectedSignature)
        => FormulaEvaluator.Evaluate(this, inputs, expectedSignature);

    private void ValidateShape()
    {
        switch (Shape)
        {
            case FormulaShape.Constant:
                RequireKind(FormulaKind.Constant);
                RequireCoefficients("value");
                break;

            case FormulaShape.Linear:
                RequireKind(FormulaKind.Calibrated);
                RequireSingleInput();
                RequireCoefficients("a", "b");
                break;

            case FormulaShape.CubicOffset:
                RequireKind(FormulaKind.Calibrated);
                RequireSingleInput();
                RequireCoefficients("a", "b");
                break;

            case FormulaShape.PowerLaw:
                RequireKind(FormulaKind.Calibrated);
                RequireSingleInput();
                RequireCoefficients("a", "b");
                break;

            case FormulaShape.RationalSaturation:
                RequireKind(FormulaKind.Calibrated);
                RequireSingleInput();
                RequireCoefficients("a", "b", "c");
                break;

            case FormulaShape.ExponentialSaturation:
                RequireKind(FormulaKind.Calibrated);
                RequireInput("x");
                RequireCoefficients("a", "b", "c");
                break;

            case FormulaShape.ResidentTiles:
                RequireKind(FormulaKind.ClosedForm);
                RequireInput("horizontalCm");
                RequireInput("chunkSizeCm");
                RequireInput("queryHistoryResidue");
                break;

            case FormulaShape.QuotaMin:
                RequireKind(FormulaKind.ClosedForm);
                RequireInput("eligible");
                RequireCoefficients("quota");
                break;

            case FormulaShape.Piecewise:
                RequireKind(FormulaKind.Piecewise);
                RequireSingleInput();
                if (Segments.Count == 0)
                    throw new ArgumentException($"分段公式 {Id} 至少需要一个分段。", nameof(Segments));
                break;

            default:
                throw new ArgumentException($"公式 {Id} 使用未知形状 {Shape}。", nameof(Shape));
        }
    }

    private void ValidateActiveState()
    {
        if (Status != FormulaStatus.Active || Kind != FormulaKind.Calibrated)
            return;

        bool hasSources = Fit.Sources is { Count: > 0 };
        if (!FormulaAcceptance.IsCalibrationAcceptable(Fit.Samples, Residual, hasSources))
        {
            throw new ArgumentException(
                $"公式 {Id} 标记为 Active，但标定验收未达标（samples={Fit.Samples}, maxRel={Residual.MaxRelError}, sources={Fit.Sources?.Count ?? 0}）。",
                nameof(Status));
        }
    }

    private void RequireKind(FormulaKind expected)
    {
        if (Kind != expected)
            throw new ArgumentException($"公式 {Id} 的 Kind={Kind} 与 Shape={Shape} 不匹配，期望 {expected}。", nameof(Kind));
    }

    private void RequireInput(string name)
    {
        if (!Inputs.Any(i => string.Equals(i.Name, name, StringComparison.Ordinal)))
            throw new ArgumentException($"公式 {Id} 缺少输入 {name}。", nameof(Inputs));
    }

    private void RequireSingleInput()
    {
        if (Inputs.Count != 1)
            throw new ArgumentException($"公式 {Id} 的单变量形状必须恰好有一个输入。", nameof(Inputs));
    }

    private void RequireCoefficients(params string[] names)
    {
        foreach (string name in names)
        {
            if (!Coefficients.Any(c => string.Equals(c.Name, name, StringComparison.Ordinal)))
                throw new ArgumentException($"公式 {Id} 缺少系数 {name}。", nameof(Coefficients));
        }
    }
}
