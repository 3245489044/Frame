using MathematicalAlgorithms.Fitting;

namespace MathematicalAlgorithms.Formula;

/// <summary>
/// 拟合请求：把「哪条曲线、什么身份、样本从哪来」交给 <see cref="FormulaCalibration"/> 产出公式契约。
/// </summary>
public sealed record FormulaCalibrationRequest(
    string Id,
    string Target,
    string Unit,
    string InputName,
    string InputUnit,
    string Method,
    FormulaFitWindow Window,
    IReadOnlyList<FormulaSource> Sources,
    FormulaRange? Domain = null,
    int Version = 1);

/// <summary>
/// 拟合 → 公式契约的唯一桥接层：按 <see cref="FormulaShape"/> 选拟合器，把 <see cref="FitResult"/>
/// 转成带系数、残差、适用域、状态与签名的 <see cref="FormulaDefinition"/>。
/// <para>拟合失败（含样本不足）不伪造公式，直接拒绝构建；适用域默认取观测 X 区间，禁止静默外推。</para>
/// </summary>
public static class FormulaCalibration
{
    public static FitResult Fit(
        FormulaShape shape,
        IEnumerable<FitSample> samples,
        FitOptions? options = null)
    {
        FitSample[] buffer = samples?.ToArray() ?? Array.Empty<FitSample>();
        if (buffer.Length == 0)
        {
            return FitResult.Fail(
                FitStatus.InsufficientSamples,
                "样本数为 0，不能拟合；公式只能停留在 provisional。",
                0);
        }

        return shape switch
        {
            FormulaShape.Linear => LinearFit.Fit(buffer, options),
            FormulaShape.CubicOffset => CubicOffsetFit.Fit(buffer, options),
            FormulaShape.PowerLaw => PowerLawFit.Fit(buffer, options),
            FormulaShape.RationalSaturation => RationalSaturationFit.Fit(buffer, options),
            FormulaShape.ExponentialSaturation => ExponentialSaturationFit.Fit(buffer, options),
            _ => FitResult.Fail(
                FitStatus.InvalidInput,
                $"形状 {shape} 不是可标定模型。",
                buffer.Length),
        };
    }

    public static FormulaDefinition Build(
        FormulaCalibrationRequest request,
        FormulaShape shape,
        FitResult result)
    {
        ArgumentNullException.ThrowIfNull(request);
        ArgumentNullException.ThrowIfNull(result);

        if (!result.Success)
        {
            throw new ArgumentException(
                $"公式 {request.Id} 拟合未成功（{result.Status}：{result.Detail}），不能构建定义。",
                nameof(result));
        }

        FormulaKind kind = KindOf(shape);
        IFitCurve curve = result.Curve!;
        FormulaRange range = request.Domain
            ?? FormulaRange.Between(result.ObservedXMin, result.ObservedXMax);

        var domain = new FormulaDomain(new SortedDictionary<string, FormulaRange>(StringComparer.Ordinal)
        {
            [request.InputName] = range,
        });

        var formula = new FormulaDefinition
        {
            Id = request.Id,
            Version = request.Version,
            Kind = kind,
            Shape = shape,
            Target = request.Target,
            Unit = request.Unit,
            Inputs = new[]
            {
                new FormulaInputSpec(request.InputName, request.InputUnit, range),
            },
            Expression = curve.Expression,
            Coefficients = curve.Coefficients
                .Select(static c => new FormulaCoefficient(c.Name, c.Value))
                .ToArray(),
            Fit = new FormulaFitRecord(
                request.Method,
                request.Window,
                result.SampleCount,
                request.Sources ?? Array.Empty<FormulaSource>()),
            Residual = result.Residuals ?? new FitResiduals(0, 0, 0, null),
            Domain = domain,
            Status = FormulaAcceptance.DetermineStatus(
                result.SampleCount,
                result.Residuals ?? new FitResiduals(0, 0, 0, null),
                request.Sources is { Count: > 0 }),
        };

        return formula.Normalize();
    }

    private static FormulaKind KindOf(FormulaShape shape)
        => shape switch
        {
            FormulaShape.Constant => FormulaKind.Constant,
            FormulaShape.Linear => FormulaKind.Calibrated,
            FormulaShape.CubicOffset => FormulaKind.Calibrated,
            FormulaShape.PowerLaw => FormulaKind.Calibrated,
            FormulaShape.RationalSaturation => FormulaKind.Calibrated,
            FormulaShape.ExponentialSaturation => FormulaKind.Calibrated,
            FormulaShape.Piecewise => FormulaKind.Piecewise,
            _ => FormulaKind.ClosedForm,
        };
}
