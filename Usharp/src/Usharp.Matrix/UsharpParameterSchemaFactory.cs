using BasicMethod.Declarative.Spec;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Matrix;

/// <summary>
/// 值型空间 → <see cref="ParameterSchema"/>（参数坐标系单一真源）：
/// 值类 <c>flag/ordinal/numeric</c> → <see cref="ParameterKind"/>，值域 <c>(min..max)</c> → <c>Min/Max</c>，
/// 坐标一律 <c>Role=State</c>（轴空间即参数态坐标系）。键型空间不产 Schema（坐标是名字、无值域，不可量化）。
/// </summary>
public static class UsharpParameterSchemaFactory
{
    /// <summary>把一个值型空间声明装配为参数坐标系。</summary>
    public static ParameterSchema Build(UsharpSpaceDecl space, ICollection<UsharpDiagnostic>? diagnostics = null)
    {
        ArgumentNullException.ThrowIfNull(space);

        var specs = new List<ParameterSpec>(space.Axes.Count);
        foreach (var axis in space.Axes)
            specs.Add(new ParameterSpec(
                axis.Name,
                ToParameterKind(axis, space, diagnostics),
                Default: 0.0,
                Min: axis.Min ?? double.NegativeInfinity,
                Max: axis.Max ?? double.PositiveInfinity));

        return new ParameterSchema(specs);
    }

    /// <summary>值类 → ParameterKind；未声明的值类按 numeric 装配并报 US0005（不静默降级）。</summary>
    public static ParameterKind ToParameterKind(
        UsharpAxis axis, UsharpSpaceDecl? owner = null, ICollection<UsharpDiagnostic>? diagnostics = null)
    {
        switch (axis.ValueKind)
        {
            case "flag": return ParameterKind.Flag;
            case "ordinal": return ParameterKind.Ordinal;
            case "numeric": return ParameterKind.Numeric;
            case null: return ParameterKind.Numeric;
            default:
                diagnostics?.Add(UsharpDiagnostic.Warn(UsharpCodes.AxisKindUnknown,
                    $"空间 {owner?.SpaceId ?? "(unknown)"} 坐标 '{axis.Name}' 声明了未知值类 '{axis.ValueKind}'"
                    + $"（合法值类：{string.Join("/", UsharpLanguage.ValueKinds)}），按 numeric 装配",
                    owner?.SourceFile ?? string.Empty, owner?.LineNumber ?? 0));
                return ParameterKind.Numeric;
        }
    }
}
