namespace Strategy.Observability.Ports;

using DataCenter.Protocol.Standards;
using Strategy.Observability.Diagnostics;

/// <summary>
/// 文本算子族（A2）：<c>trim</c> / <c>substring{start,length}</c> / <c>replace{old,replacement}</c>。
/// <para>口径：全部以「文本形态」（<see cref="PortValues.Text"/>）参与；源缺失 → null（不静默成空串）；
/// 参数缺失 / 越界 / 空 old → fail-fast（禁静默截断或回落）。</para>
/// </summary>
internal static partial class ScriptedExprEvaluator
{
    /// <summary>两侧空白裁剪（<c>trim</c>）。</summary>
    private static string? Trim(
        ExprStandard source, EvalScope scope, IPortInput input, object?[] deps, string field)
        => PortValues.Text(Sub(source, scope, input, deps, field))?.Trim();

    /// <summary>子串（<c>substring</c> + <c>start</c>[+ <c>length</c>]）：下标 / 长度越界即抛。</summary>
    private static string? Substring(
        ExprStandard expr, ExprStandard source, EvalScope scope, IPortInput input, object?[] deps, string field)
    {
        var text = PortValues.Text(Sub(source, scope, input, deps, field));
        if (text is null) return null;

        var start = IndexOf(expr.SubstringStart, scope, input, deps, field, "start") ?? 0;

        if (start < 0 || start > text.Length)
            throw new PortBindingViolationException(
                scope.PortKey,
                $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的 substring.start={start} 越界（文本长度 {text.Length}）");

        if (expr.SubstringLength is null) return text.Substring(start);

        var length = IndexOf(expr.SubstringLength, scope, input, deps, field, "length")
                     ?? throw new PortBindingViolationException(
                         scope.PortKey,
                         $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的 substring.length 求值为空");

        if (length < 0 || start + length > text.Length)
            throw new PortBindingViolationException(
                scope.PortKey,
                $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的 substring.length={length} 越界" +
                $"（start={start}，文本长度 {text.Length}）");

        return text.Substring(start, length);
    }

    /// <summary>文本替换（<c>replace</c> + <c>old</c> + <c>replacement</c>）：<c>old</c> 空即抛。</summary>
    private static string? Replace(
        ExprStandard expr, ExprStandard source, EvalScope scope, IPortInput input, object?[] deps, string field)
    {
        var text = PortValues.Text(Sub(source, scope, input, deps, field));
        if (text is null) return null;

        var old = PortValues.Text(Sub(expr.ReplaceOld!, scope, input, deps, field));

        if (string.IsNullOrEmpty(old))
            throw new PortBindingViolationException(
                scope.PortKey,
                $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的 replace.old 须为非空文本（空即无法定位替换点）");

        var replacement = expr.ReplaceReplacement is { } target
            ? PortValues.Text(Sub(target, scope, input, deps, field)) ?? string.Empty
            : string.Empty;

        return text.Replace(old, replacement, StringComparison.Ordinal);
    }

    /// <summary>整型下标取值（非整型 / 缺失 → fail-fast）。</summary>
    private static int? IndexOf(
        ExprStandard? operand, EvalScope scope, IPortInput input, object?[] deps, string field, string name)
    {
        if (operand is null) return null;

        var value = Sub(operand, scope, input, deps, field);
        if (value is null) return null;

        if (PortValues.Number(value) is not { } number || number != Math.Truncate(number))
            throw new PortBindingViolationException(
                scope.PortKey,
                $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的 substring.{name} 须为整数（当前 '{value}'）");

        return (int)number;
    }
}