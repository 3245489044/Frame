using System.Text.Json.Serialization;
using MathematicalAlgorithms.Formula;

namespace MathematicalAlgorithms.FormulaFit;

/// <summary>单个拟合目标。</summary>
public sealed record FormulaFitTargetPlan
{
    public required string Id { get; init; }

    public int Version { get; init; } = 1;

    public FormulaKind Kind { get; init; } = FormulaKind.Calibrated;

    public required FormulaShape Shape { get; init; }

    public required string Target { get; init; }

    public required string Unit { get; init; }

    /// <summary>x 的 JSON Pointer，例如 /conditions/visibleQuota。</summary>
    public required string X { get; init; }

    /// <summary>y 的 JSON Pointer，例如 /metrics/frame.sync/p95Ms。</summary>
    public required string Y { get; init; }

    /// <summary>y 为数组/多轮时的聚合方式：median（默认）/mean/min/max/first/last。</summary>
    public string Aggregate { get; init; } = "median";

    /// <summary>用于生成 fit.window.conditions 的 JSON Pointer 列表。</summary>
    public IReadOnlyList<string> GroupBy { get; init; } = Array.Empty<string>();

    /// <summary>样本筛选条件；全部命中才参与拟合。</summary>
    public IReadOnlyList<FormulaTargetFilter> Filter { get; init; } = Array.Empty<FormulaTargetFilter>();

    public IReadOnlyList<FormulaFitInputPlan> Inputs { get; init; } = Array.Empty<FormulaFitInputPlan>();

    public int MinimumSamples { get; init; } = FormulaAcceptance.MinimumCalibrationSamples;

    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(Id))
            throw new InvalidOperationException("target.id 不能为空。");

        if (Version <= 0)
            throw new InvalidOperationException($"target {Id} 的 version 必须为正。");

        if (Kind != FormulaKind.Calibrated)
            throw new InvalidOperationException($"target {Id} 的 kind={Kind}；Phase 3 仅支持 calibrated。");

        if (Shape is not (FormulaShape.Linear or FormulaShape.CubicOffset
            or FormulaShape.PowerLaw or FormulaShape.RationalSaturation))
        {
            throw new InvalidOperationException($"target {Id} 的 shape={Shape} 不受离线拟合支持。");
        }

        if (string.IsNullOrWhiteSpace(Target))
            throw new InvalidOperationException($"target {Id} 的 target 不能为空。");

        if (string.IsNullOrWhiteSpace(Unit))
            throw new InvalidOperationException($"target {Id} 的 unit 不能为空。");

        if (!IsPointer(X) || !IsPointer(Y))
            throw new InvalidOperationException($"target {Id} 的 x/y 必须是 JSON Pointer（以 / 开头）。");

        if (Inputs is null || Inputs.Count != 1)
            throw new InvalidOperationException($"target {Id} 的单变量形状必须恰好声明一个 input。");

        FormulaFitInputPlan input = Inputs[0];
        if (string.IsNullOrWhiteSpace(input.Name))
            throw new InvalidOperationException($"target {Id} 的 input.name 不能为空。");

        if (string.IsNullOrWhiteSpace(input.Unit))
            throw new InvalidOperationException($"target {Id} 的 input.unit 不能为空。");

        if (input.Min.HasValue && input.Max.HasValue && input.Min.Value > input.Max.Value)
            throw new InvalidOperationException($"target {Id} 的 input 区间反向。");

        if (input.Values is { Count: 0 })
            throw new InvalidOperationException($"target {Id} 的 input.values 不能为空数组。");

        if (MinimumSamples < FormulaAcceptance.MinimumCalibrationSamples)
        {
            throw new InvalidOperationException(
                $"target {Id} 的 minimumSamples={MinimumSamples} 小于标定下限 {FormulaAcceptance.MinimumCalibrationSamples}。");
        }

        if (!IsSupportedAggregate(Aggregate))
            throw new InvalidOperationException($"target {Id} 的 aggregate={Aggregate} 不受支持。");

        foreach (string pointer in GroupBy ?? Array.Empty<string>())
        {
            if (!IsPointer(pointer))
                throw new InvalidOperationException($"target {Id} 的 groupBy 不是 JSON Pointer：{pointer}。");
        }

        foreach (FormulaTargetFilter filter in Filter ?? Array.Empty<FormulaTargetFilter>())
        {
            if (filter is null || !IsPointer(filter.Path))
                throw new InvalidOperationException($"target {Id} 的 filter.path 不是 JSON Pointer。");

            if (filter.EqualsValue is null)
                throw new InvalidOperationException($"target {Id} 的 filter.equals 不能为空。");
        }
    }

    public static bool IsSupportedAggregate(string value)
        => value is "median" or "mean" or "min" or "max" or "first" or "last";

    private static bool IsPointer(string value)
        => !string.IsNullOrWhiteSpace(value) && value[0] == '/';
}

/// <summary>筛选条件：路径解析出的字符串与 equals 逐字符相等。</summary>
public sealed record FormulaTargetFilter(
    string Path,
    [property: JsonPropertyName("equals")] string EqualsValue);

/// <summary>公式输入声明（用于生成 FormulaInputSpec）。</summary>
public sealed record FormulaFitInputPlan(
    string Name,
    string Unit,
    double? Min = null,
    double? Max = null,
    IReadOnlyList<double>? Values = null)
{
    public FormulaRange ToRange() => new(Min, Max, Values);

    public FormulaInputSpec ToSpec() => new(Name, Unit, ToRange());
}
