namespace Strategy.Observability.Ports;

using DataCenter.Protocol.Standards;

using System.Reflection;
using System.Text.Json;
using Strategy.Observability.Diagnostics;


/// <summary>
/// 投影表达式求值器（E22 自证对象）：源值 → 算子递归求值；未知形态 / 悬空引用 → fail-fast。
/// <para>算子语义（规范 §5）：<c>count</c> 计数、<c>notNull</c> 非空、<c>text</c> 文本、<c>typeName</c>/<c>message</c>/<c>thrown</c>
/// 异常三态、<c>concat</c> 拼接、<c>contains</c> 包含、<c>all</c>/<c>any</c> 量词、<c>spread</c> 动态键展开、
/// <c>countEqual</c> 取等、<c>collect</c> 收集槽逐项投影。</para>
/// </summary>
internal static partial class ScriptedExprEvaluator
{
    /// <summary>求值（<paramref name="field"/> 仅用于诊断定位）。</summary>
    internal static object? Evaluate(
        ExprStandard expr, EvalScope scope, IPortInput input, object?[] deps, string field)
    {
        var context = $"投影字段 '{field}'";

        if (expr.Source is { } source) return ScriptedValue.Resolve(source, scope, input, deps, context);

        if (expr.Count is { } count) return Count(Sub(count, scope, input, deps, field), scope.PortKey, field);
        if (expr.NotNull is { } notNull) return Sub(notNull, scope, input, deps, field) is not null;
        if (expr.AsText is { } asText) return PortValues.Text(Sub(asText, scope, input, deps, field));
        if (expr.TypeName is { } typeName) return TypeNameOf(Sub(typeName, scope, input, deps, field));
        if (expr.Message is { } message) return (Sub(message, scope, input, deps, field) as Exception)?.Message;
        if (expr.Thrown is { } thrown) return Sub(thrown, scope, input, deps, field) is Exception;
        if (expr.Concat is { } concat) return Concat(expr, concat, scope, input, deps, field);
        if (expr.Contains is { } contains) return Contains(expr, contains, scope, input, deps, field);
        if (expr.All is { } all) return Quantify(expr, all, scope, input, deps, field, every: true);
        if (expr.Any is { } any) return Quantify(expr, any, scope, input, deps, field, every: false);
        if (expr.Spread is { } spread) return Spread(expr, spread, scope, input, deps, field);
        if (expr.CountEqual is { Count: 2 } equal) return CountEqual(equal, scope, input, deps, field);
        if (expr.IsNull is { } isNull) return Sub(isNull, scope, input, deps, field) is null;
        if (expr.Not is { } not) return Not(Sub(not, scope, input, deps, field));
        if (expr.AllOf is { } allOf) return Junction(allOf, scope, input, deps, field, every: true);
        if (expr.AnyOf is { } anyOf) return Junction(anyOf, scope, input, deps, field, every: false);
        if (expr.Eq is { } eq) return Compare(eq, scope, input, deps, field, "eq");
        if (expr.Ne is { } ne) return Compare(ne, scope, input, deps, field, "ne");
        if (expr.Gt is { } gt) return Compare(gt, scope, input, deps, field, "gt");
        if (expr.Ge is { } ge) return Compare(ge, scope, input, deps, field, "ge");
        if (expr.Lt is { } lt) return Compare(lt, scope, input, deps, field, "lt");
        if (expr.Le is { } le) return Compare(le, scope, input, deps, field, "le");
        if (expr.Invoke is { } call)
            return ScriptedInvokeRunner.Run(scope.PortKey, call, specs: null, scope, input, deps).Result;

        if (expr.First is { } first) return First(expr, first, scope, input, deps, field, expr.FirstOrNull);
        if (expr.Last is { } last) return Last(expr, last, scope, input, deps, field);
        if (expr.Agg is { } agg) return Aggregate(expr, agg, scope, input, deps, field);
        if (expr.Filter is { } filter) return Filter(expr, filter, scope, input, deps, field);
        if (expr.Distinct is { } distinct) return Distinct(distinct, scope, input, deps, field);
        if (expr.Fold is { } fold) return Fold(expr, fold, scope, input, deps, field);
        if (expr.Dict is { } dict) return Dict(expr, dict, scope, input, deps, field);
        if (expr.Parts is { } parts) return ConcatParts(expr, parts, scope, input, deps, field);
        if (expr.SequenceEqual is { Count: 2 } sequence) return SequenceEqual(sequence, scope, input, deps, field);
        if (expr.SameReference is { Count: 2 } same) return SameReference(same, scope, input, deps, field);
        if (expr.Coalesce is { Count: 2 } coalesce) return Coalesce(coalesce, scope, input, deps, field);

        if (expr.Collect is { Length: > 0 } collect) return Collect(expr, collect, scope, input, deps, field);

        throw new PortBindingViolationException(
            scope.PortKey, $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的表达式未声明来源或算子");
    }
}
