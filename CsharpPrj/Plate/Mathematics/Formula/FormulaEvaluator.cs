namespace MathematicalAlgorithms.Formula;

/// <summary>
/// 公式求值器：先验签、再验域、最后按 <see cref="FormulaShape"/> 分派。
/// <para>不做字符串表达式解析，不夹取、不外推；预期失败统一转为 <see cref="FormulaEvaluation.OutOfDomain"/>。</para>
/// </summary>
public static class FormulaEvaluator
{
    public static FormulaEvaluation Evaluate(
        FormulaDefinition definition,
        IReadOnlyDictionary<string, double> inputs,
        long expectedSignature)
    {
        ArgumentNullException.ThrowIfNull(definition);
        ArgumentNullException.ThrowIfNull(inputs);

        if (definition.Signature != expectedSignature)
            return new FormulaEvaluation.Stale(expectedSignature, definition.Signature);

        if (!definition.Domain.TryValidate(inputs, out string? violatedInput, out string? reason))
            return new FormulaEvaluation.OutOfDomain(reason ?? "输入越域。", violatedInput ?? "unknown");

        try
        {
            double value = EvaluateShape(definition, inputs);
            if (!double.IsFinite(value))
                return new FormulaEvaluation.OutOfDomain("求值结果非有限。", definition.Target);

            return new FormulaEvaluation.Hit(value, definition.Signature);
        }
        catch (FormulaEvaluationException ex)
        {
            return new FormulaEvaluation.OutOfDomain(ex.Message, ex.InputName);
        }
    }

    private static double EvaluateShape(
        FormulaDefinition definition,
        IReadOnlyDictionary<string, double> inputs)
        => definition.Shape switch
        {
            FormulaShape.Constant => Coefficient(definition, "value"),
            FormulaShape.Linear => EvaluateLinear(definition, inputs),
            FormulaShape.CubicOffset => EvaluateCubicOffset(definition, inputs),
            FormulaShape.PowerLaw => EvaluatePowerLaw(definition, inputs),
            FormulaShape.RationalSaturation => EvaluateRationalSaturation(definition, inputs),
            FormulaShape.ResidentTiles => EvaluateResidentTiles(inputs),
            FormulaShape.QuotaMin => Math.Min(Coefficient(definition, "quota"), Input(inputs, "eligible")),
            FormulaShape.Piecewise => EvaluatePiecewise(definition, inputs),
            _ => throw new FormulaEvaluationException($"未知公式形状 {definition.Shape}。", definition.Target),
        };

    private static double EvaluateLinear(
        FormulaDefinition definition,
        IReadOnlyDictionary<string, double> inputs)
    {
        double a = Coefficient(definition, "a");
        double b = Coefficient(definition, "b");
        double x = Input(inputs, "x");
        return a * x + b;
    }

    private static double EvaluateCubicOffset(
        FormulaDefinition definition,
        IReadOnlyDictionary<string, double> inputs)
    {
        double a = Coefficient(definition, "a");
        double b = Coefficient(definition, "b");
        double x = Input(inputs, "x");
        return a * x * x * x + b;
    }

    private static double EvaluatePowerLaw(
        FormulaDefinition definition,
        IReadOnlyDictionary<string, double> inputs)
    {
        double a = Coefficient(definition, "a");
        double b = Coefficient(definition, "b");
        double x = Input(inputs, "x");
        if (x <= 0)
            throw new FormulaEvaluationException("幂律要求 x>0。", "x");

        return a * Math.Pow(x, b);
    }

    private static double EvaluateRationalSaturation(
        FormulaDefinition definition,
        IReadOnlyDictionary<string, double> inputs)
    {
        double a = Coefficient(definition, "a");
        double b = Coefficient(definition, "b");
        double c = Coefficient(definition, "c");
        double x = Input(inputs, "x");
        double denominator = b + x;
        if (Math.Abs(denominator) <= 1e-15)
            throw new FormulaEvaluationException("有理饱和分母接近零。", "x");

        return a * x / denominator + c;
    }

    private static double EvaluateResidentTiles(IReadOnlyDictionary<string, double> inputs)
    {
        double horizontalCm = Input(inputs, "horizontalCm");
        double chunkSizeCm = Input(inputs, "chunkSizeCm");
        double residue = Input(inputs, "queryHistoryResidue");

        if (chunkSizeCm <= 0)
            throw new FormulaEvaluationException("chunkSizeCm 必须为正。", "chunkSizeCm");

        double horizontal = Math.Ceiling(horizontalCm / chunkSizeCm);
        double side = 2 * horizontal + 1;
        return side * side + residue;
    }

    private static double EvaluatePiecewise(
        FormulaDefinition definition,
        IReadOnlyDictionary<string, double> inputs)
    {
        double x = Input(inputs, "x");
        foreach (FormulaSegment segment in definition.Segments.OrderBy(static s => s.MinX))
        {
            if (x >= segment.MinX && x <= segment.MaxX)
                return segment.Slope * x + segment.Intercept;
        }

        throw new FormulaEvaluationException("x 不在任何分段内。", "x");
    }

    private static double Input(IReadOnlyDictionary<string, double> inputs, string name)
    {
        if (!inputs.TryGetValue(name, out double value))
            throw new FormulaEvaluationException($"缺少输入 {name}。", name);

        if (!double.IsFinite(value))
            throw new FormulaEvaluationException($"输入 {name} 不是有限值。", name);

        return value;
    }

    private static double Coefficient(FormulaDefinition definition, string name)
    {
        foreach (FormulaCoefficient coefficient in definition.Coefficients)
        {
            if (string.Equals(coefficient.Name, name, StringComparison.Ordinal))
                return coefficient.Value;
        }

        throw new FormulaEvaluationException($"缺少系数 {name}。", name);
    }

    private sealed class FormulaEvaluationException : Exception
    {
        public FormulaEvaluationException(string message, string inputName)
            : base(message)
        {
            InputName = inputName;
        }

        public string InputName { get; }
    }
}
