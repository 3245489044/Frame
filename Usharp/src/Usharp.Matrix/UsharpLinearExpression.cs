using System.Globalization;
using Usharp.Syntax;

namespace Usharp.Matrix;

/// <summary>线性项：<c>weight · axis</c>（同一坐标重复出现时在解析期合并，权重相加）。</summary>
public readonly record struct UsharpLinearTerm(string Axis, double Weight);

/// <summary>
/// 矩阵行的线性表达式 <c>bias + Σ weight·axis</c> —— <c>[Matrix]</c> 行右式的唯一解释器。
/// 文法：<c>factor (('+'|'-') factor)*</c>；<c>factor ::= number | [number '*'] axis | axis ['*' number]</c>。
/// 坐标名字符集 = <c>UsharpIdentifier</c>（单一真源：声明面坐标行 / 行内引用 / 本词法三处共用），避免两处口径分叉。
/// 本类型只做词法/算术解析，不承载任何矩阵语义（线性变换语义由 ParameterMatrix 承担）。
/// </summary>
public sealed record UsharpLinearExpression(double Bias, IReadOnlyList<UsharpLinearTerm> Terms)
{
    public static readonly UsharpLinearExpression Empty = new(0, Array.Empty<UsharpLinearTerm>());

    /// <summary>引用到的坐标（合并重复后、首次出现序）。</summary>
    public IReadOnlyList<string> Axes => Terms.Select(t => t.Axis).ToList();

    /// <summary>解析线性表达式；失败时 <paramref name="error"/> 给出可归因的失败原因。</summary>
    public static bool TryParse(string? text, out UsharpLinearExpression expression, out string? error)
    {
        expression = Empty;
        error = null;

        var src = text?.Trim() ?? string.Empty;
        if (src.Length == 0)
        {
            error = "行右式为空";
            return false;
        }

        var bias = 0.0;
        var weights = new List<(string Axis, double Weight)>();
        var i = 0;

        if (!TryReadFactor(src, ref i, out var first, out error)) return false;
        Accumulate(first, 1.0, ref bias, weights);

        while (true)
        {
            SkipSpaces(src, ref i);
            if (i >= src.Length) break;

            var op = src[i];
            if (op != '+' && op != '-')
            {
                error = $"无法识别的记号 '{op}'（仅支持 + - * 与坐标名/数字）";
                return false;
            }
            i++;

            if (!TryReadFactor(src, ref i, out var factor, out error)) return false;
            Accumulate(factor, op == '-' ? -1.0 : 1.0, ref bias, weights);
        }

        expression = new UsharpLinearExpression(
            bias, weights.Select(w => new UsharpLinearTerm(w.Axis, w.Weight)).ToList());
        return true;
    }

    private static void Accumulate(
        (double? Number, string? Axis, double Coefficient) factor,
        double sign,
        ref double bias,
        List<(string Axis, double Weight)> weights)
    {
        if (factor.Axis is null)
        {
            bias += sign * (factor.Number ?? 0.0);
            return;
        }

        for (var k = 0; k < weights.Count; k++)
        {
            if (!string.Equals(weights[k].Axis, factor.Axis, StringComparison.Ordinal)) continue;
            weights[k] = (weights[k].Axis, weights[k].Weight + sign * factor.Coefficient);
            return;
        }
        weights.Add((factor.Axis, sign * factor.Coefficient));
    }

    private static bool TryReadFactor(
        string src, ref int i, out (double? Number, string? Axis, double Coefficient) factor, out string? error)
    {
        factor = (null, null, 1.0);
        error = null;
        SkipSpaces(src, ref i);
        if (i >= src.Length)
        {
            error = "行右式不完整（缺少坐标或数字）";
            return false;
        }

        if (TryReadNumber(src, ref i, out var number))
        {
            SkipSpaces(src, ref i);
            if (i < src.Length && src[i] == '*')
            {
                i++;
                SkipSpaces(src, ref i);
                if (!TryReadAxis(src, ref i, out var axis))
                {
                    error = "'*' 右侧须为坐标名";
                    return false;
                }
                factor = (null, axis, number);
                return true;
            }
            factor = (number, null, 0.0);
            return true;
        }

        if (TryReadAxis(src, ref i, out var name))
        {
            SkipSpaces(src, ref i);
            if (i < src.Length && src[i] == '*')
            {
                i++;
                SkipSpaces(src, ref i);
                if (!TryReadNumber(src, ref i, out var coefficient))
                {
                    error = "'*' 右侧须为数字系数";
                    return false;
                }
                factor = (null, name, coefficient);
                return true;
            }
            factor = (null, name, 1.0);
            return true;
        }

        error = $"位置 {i} 处既不是坐标名也不是数字";
        return false;
    }

    private static void SkipSpaces(string src, ref int i)
    {
        while (i < src.Length && char.IsWhiteSpace(src[i])) i++;
    }

    private static bool TryReadNumber(string src, ref int i, out double value)
    {
        value = 0.0;
        var start = i;
        while (i < src.Length && (char.IsAsciiDigit(src[i]) || src[i] == '.')) i++;
        if (i == start) return false;
        return double.TryParse(src.AsSpan(start, i - start), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    private static bool TryReadAxis(string src, ref int i, out string axis)
        => UsharpIdentifier.TryRead(src, ref i, out axis);
}
