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
    /// <summary>算子求值委托（<paramref name="field"/> 仅用于诊断定位）。</summary>
    private delegate object? Evaluator(ExprStandard expr, EvalScope scope, IPortInput input, object?[] deps, string field);

    /// <summary>
    /// 算子注册表（**声明序 = 判别序**，与既有 if 链逐字同序）：命中即求值。
    /// <para>取代 30+ 条 <c>if (expr.X is {})</c> 链（R3）；新增算子 = ① <c>ExprSlots</c> 一行声明 + ② 本表一行（或复用同组处理器）。</para>
    /// </summary>
    private static readonly IReadOnlyList<(Func<ExprStandard, bool> Match, Evaluator Evaluate)> Operators =
    [
        (e => e.Source is not null, (e, scope, input, deps, field) =>
            ScriptedValue.Resolve(e.Source!, scope, input, deps, $"投影字段 '{field}'")),
        (e => e.Count is not null, (e, scope, input, deps, field) =>
            Count(Sub(e.Count!, scope, input, deps, field), scope.PortKey, field)),
        (e => e.NotNull is not null, (e, scope, input, deps, field) =>
            Sub(e.NotNull!, scope, input, deps, field) is not null),
        (e => e.AsText is not null, (e, scope, input, deps, field) =>
            PortValues.Text(Sub(e.AsText!, scope, input, deps, field))),
        (e => e.TypeName is not null, (e, scope, input, deps, field) =>
            TypeNameOf(Sub(e.TypeName!, scope, input, deps, field))),
        (e => e.Message is not null, (e, scope, input, deps, field) =>
            (Sub(e.Message!, scope, input, deps, field) as Exception)?.Message),
        (e => e.Thrown is not null, (e, scope, input, deps, field) =>
            Sub(e.Thrown!, scope, input, deps, field) is Exception),
        (e => e.Concat is not null, (e, scope, input, deps, field) =>
            Concat(e, e.Concat!, scope, input, deps, field)),
        (e => e.Contains is not null, (e, scope, input, deps, field) =>
            Contains(e, e.Contains!, scope, input, deps, field)),
        (e => e.All is not null, (e, scope, input, deps, field) =>
            Quantify(e, e.All!, scope, input, deps, field, every: true)),
        (e => e.Any is not null, (e, scope, input, deps, field) =>
            Quantify(e, e.Any!, scope, input, deps, field, every: false)),
        (e => e.Spread is not null, (e, scope, input, deps, field) =>
            Spread(e, e.Spread!, scope, input, deps, field)),
        (e => e.CountEqual is { Count: 2 }, (e, scope, input, deps, field) =>
            CountEqual(e.CountEqual!, scope, input, deps, field)),
        (e => e.IsNull is not null, (e, scope, input, deps, field) =>
            Sub(e.IsNull!, scope, input, deps, field) is null),
        (e => e.Not is not null, (e, scope, input, deps, field) =>
            Not(Sub(e.Not!, scope, input, deps, field))),
        (e => e.AllOf is not null, (e, scope, input, deps, field) =>
            Junction(e.AllOf!, scope, input, deps, field, every: true)),
        (e => e.AnyOf is not null, (e, scope, input, deps, field) =>
            Junction(e.AnyOf!, scope, input, deps, field, every: false)),
        (e => e.Eq is not null, (e, scope, input, deps, field) =>
            Compare(e.Eq!, scope, input, deps, field, "eq")),
        (e => e.Ne is not null, (e, scope, input, deps, field) =>
            Compare(e.Ne!, scope, input, deps, field, "ne")),
        (e => e.Gt is not null, (e, scope, input, deps, field) =>
            Compare(e.Gt!, scope, input, deps, field, "gt")),
        (e => e.Ge is not null, (e, scope, input, deps, field) =>
            Compare(e.Ge!, scope, input, deps, field, "ge")),
        (e => e.Lt is not null, (e, scope, input, deps, field) =>
            Compare(e.Lt!, scope, input, deps, field, "lt")),
        (e => e.Le is not null, (e, scope, input, deps, field) =>
            Compare(e.Le!, scope, input, deps, field, "le")),
        (e => e.Invoke is not null, (e, scope, input, deps, field) =>
            ScriptedInvokeRunner.Run(scope.PortKey, e.Invoke!, specs: null, scope, input, deps).Result),
        (e => e.First is not null, (e, scope, input, deps, field) =>
            First(e, e.First!, scope, input, deps, field, e.FirstOrNull)),
        (e => e.Last is not null, (e, scope, input, deps, field) =>
            Last(e, e.Last!, scope, input, deps, field)),
        (e => e.Agg is not null, (e, scope, input, deps, field) =>
            Aggregate(e, e.Agg!, scope, input, deps, field)),
        (e => e.Filter is not null, (e, scope, input, deps, field) =>
            Filter(e, e.Filter!, scope, input, deps, field)),
        (e => e.Select is not null, (e, scope, input, deps, field) =>
            Select(e, e.Select!, scope, input, deps, field)),
        (e => e.Distinct is not null, (e, scope, input, deps, field) =>
            Distinct(e.Distinct!, scope, input, deps, field)),
        (e => e.Fold is not null, (e, scope, input, deps, field) =>
            Fold(e, e.Fold!, scope, input, deps, field)),
        (e => e.Dict is not null, (e, scope, input, deps, field) =>
            Dict(e, e.Dict!, scope, input, deps, field)),
        (e => e.Parts is not null, (e, scope, input, deps, field) =>
            ConcatParts(e, e.Parts!, scope, input, deps, field)),
        (e => e.SequenceEqual is { Count: 2 }, (e, scope, input, deps, field) =>
            SequenceEqual(e.SequenceEqual!, scope, input, deps, field)),
        (e => e.SameReference is { Count: 2 }, (e, scope, input, deps, field) =>
            SameReference(e.SameReference!, scope, input, deps, field)),
        (e => e.Coalesce is { Count: 2 }, (e, scope, input, deps, field) =>
            Coalesce(e.Coalesce!, scope, input, deps, field)),
        (e => e.Capture is not null, (e, scope, input, deps, field) =>
            Capture(e.Capture!, scope, deps, field)),
        (e => e.CaptureCount is not null, (e, scope, input, deps, field) =>
            CaptureCount(e.CaptureCount!, scope, deps, field)),
        (e => e.FakeCount is not null, (e, scope, input, deps, field) =>
            FakeCount(e.FakeCount!, scope, deps, field)),
        (e => e.Substring is not null, (e, scope, input, deps, field) =>
            Substring(e, e.Substring!, scope, input, deps, field)),
        (e => e.Replace is not null, (e, scope, input, deps, field) =>
            Replace(e, e.Replace!, scope, input, deps, field)),
        (e => e.Trim is not null, (e, scope, input, deps, field) =>
            Trim(e.Trim!, scope, input, deps, field)),
        (e => e.Collect is { Length: > 0 }, (e, scope, input, deps, field) =>
            Collect(e, e.Collect!, scope, input, deps, field)),
    ];

    /// <summary>求值（<paramref name="field"/> 仅用于诊断定位）。</summary>
    internal static object? Evaluate(
        ExprStandard expr, EvalScope scope, IPortInput input, object?[] deps, string field)
    {
        foreach (var (match, evaluate) in Operators)
            if (match(expr))
                return evaluate(expr, scope, input, deps, field);

        throw new PortBindingViolationException(
            scope.PortKey, $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的表达式未声明来源或算子");
    }
}
