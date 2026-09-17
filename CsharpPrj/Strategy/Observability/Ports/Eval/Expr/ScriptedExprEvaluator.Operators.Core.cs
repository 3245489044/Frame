namespace Strategy.Observability.Ports;

using System.Reflection;
using System.Text.Json;
using Strategy.Observability.Diagnostics;


internal static partial class ScriptedExprEvaluator
{
    private static object? Sub(
        ScriptedExpr expr, EvalScope scope, IPortInput input, object?[] deps, string field)
        => Evaluate(expr, scope, input, deps, field);

    private static int Count(object? value, string portKey, string field)
        => PortValues.TryCount(value)
           ?? throw new PortBindingViolationException(
               portKey, $"端口 '{portKey}' 的投影字段 '{field}' 的 count 不适用于 {value!.GetType().Name}");

    private static string? TypeNameOf(object? value) => value switch
    {
        Exception exception => exception.GetType().Name,
        Type type => type.Name,
        null => null,
        _ => value.GetType().Name,
    };

    private static string Concat(
        ScriptedExpr expr, IReadOnlyList<ScriptedExpr> parts,
        EvalScope scope, IPortInput input, object?[] deps, string field)
        => string.Join(expr.Separator ?? string.Empty,
            parts.Select(part => PortValues.Text(Sub(part, scope, input, deps, field)) ?? string.Empty));

    /// <summary>
    /// 拼接段（W19 文本模板）：逐段求门控（求值为假 / <c>⊥</c> 即**省略该段**）→ 取文本 → 按分隔符拼接。
    /// <para>条件行省略（「有则一行、无则整行消失」）的声明等价；缺值段按空串参与（与 <c>concat</c> 同口径）。</para>
    /// </summary>
    private static string ConcatParts(
        ScriptedExpr expr, IReadOnlyList<ScriptedConcatPartSpec> parts,
        EvalScope scope, IPortInput input, object?[] deps, string field)
    {
        var texts = new List<string>(parts.Count);
        foreach (var part in parts)
        {
            if (part.When is { } gate && !PortValues.Truthy(Sub(gate, scope, input, deps, field))) continue;

            texts.Add(PortValues.Text(Sub(part.Expr, scope, input, deps, field)) ?? string.Empty);
        }

        return string.Join(expr.Separator ?? string.Empty, texts);
    }

    /// <summary>
    /// 去重成集（W25）：遍历来源元素 → 按值等价（<see cref="Equal"/>）保留**首次出现** → 稳定序数组。
    /// <para>与 <c>count</c> / <c>all</c> 组合即「去重计数 / 去重判定」（对齐夹具 <c>Distinct(Ordinal).Count()</c>）。</para>
    /// </summary>
    private static object?[] Distinct(
        ScriptedExpr over, EvalScope scope, IPortInput input, object?[] deps, string field)
    {
        var kept = new List<object?>();
        foreach (var item in PortValues.Elements(Sub(over, scope, input, deps, field)))
        {
            if (kept.Any(existing => Equal(existing, item))) continue;

            kept.Add(item);
        }

        return kept.ToArray();
    }

    /// <summary>
    /// 序列逐元素相等（W23：<c>sequenceEqual:[左,右]</c>）：长度相同 ∧ 逐位值等价（<see cref="Equal"/>，序敏感）；
    /// 任一侧缺值 → <c>⊥</c>（null），与二元比较同口径。
    /// </summary>
    private static object? SequenceEqual(
        IReadOnlyList<ScriptedExpr> operands, EvalScope scope, IPortInput input, object?[] deps, string field)
    {
        var left = Sub(operands[0], scope, input, deps, field);
        var right = Sub(operands[1], scope, input, deps, field);

        if (left is null || right is null) return null;

        var expected = PortValues.Elements(left).ToArray();
        var actual = PortValues.Elements(right).ToArray();
        if (expected.Length != actual.Length) return false;

        for (var i = 0; i < expected.Length; i++)
            if (!Equal(expected[i], actual[i])) return false;

        return true;
    }

    /// <summary>
    /// 折叠累积（W21：<c>fold{over,as,init,step}</c>）：初值 → 逐元素对**累积值**调用 <c>step.method</c>
    /// （元素以帧名 <c>as</c> 入栈，体内 <c>from:item</c> 引用）→ 返回累积值（<c>step.rebind</c> 决定以返回值替换）。
    /// <para>调用单源复用 <see cref="ScriptedInvokeRunner.RunOn"/>（与 <c>chain</c> 同一实例调用口径）。</para>
    /// </summary>
    private static object? Fold(
        ScriptedExpr expr, ScriptedExpr over, EvalScope scope, IPortInput input, object?[] deps, string field)
    {
        var step = expr.FoldStep ?? throw new PortBindingViolationException(
            scope.PortKey, $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的 fold 缺 step（累积调用声明）");

        if (string.IsNullOrWhiteSpace(step.Method))
            throw new PortBindingViolationException(
                scope.PortKey, $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的 fold.step 缺 method");

        var accumulator = ScriptedValue.Resolve(
            expr.FoldInit ?? throw new PortBindingViolationException(
                scope.PortKey, $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的 fold 缺 init（初值来源）"),
            scope, input, deps, $"投影字段 '{field}' 的 fold.init");

        var frame = expr.FoldAs is { Length: > 0 } name ? name : EvalScope.RowFrame;
        var index = 0;

        foreach (var item in PortValues.Elements(Sub(over, scope, input, deps, field)))
        {
            scope.Push(frame, item, index);
            try
            {
                if (accumulator is null)
                    throw new PortBindingViolationException(
                        scope.PortKey,
                        $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的 fold 累积值为 null（不可调用 '{step.Method}'）");

                var call = new ScriptedInvokeSpec(Method: step.Method, Args: step.Args);
                var result = ScriptedInvokeRunner
                    .RunOn(scope.PortKey, call, accumulator, scope, input, deps).Result;

                if (step.Rebind) accumulator = result;
            }
            finally
            {
                scope.Pop();
            }

            index++;
        }

        return accumulator;
    }

    private static bool Contains(
        ScriptedExpr expr, ScriptedExpr container, EvalScope scope, IPortInput input, object?[] deps, string field)
    {
        var needle = expr.Of is { } of
            ? Sub(of, scope, input, deps, field)
            : throw new PortBindingViolationException(
                scope.PortKey, $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的 contains 缺少 of（被含元素）");

        var value = Sub(container, scope, input, deps, field);

        return value switch
        {
            null => false,
            string text => text.Contains(PortValues.Text(needle) ?? string.Empty, StringComparison.Ordinal),
            System.Collections.IDictionary map => map.Contains(needle),
            _ => PortValues.Elements(value).Any(item => Equal(item, needle)),
        };
    }

    private static bool Quantify(
        ScriptedExpr expr, ScriptedExpr over, EvalScope scope, IPortInput input, object?[] deps,
        string field, bool every)
    {
        var items = PortValues.Elements(Sub(over, scope, input, deps, field)).ToArray();
        if (items.Length == 0) return every;

        var predicate = Predicate(expr, scope);
        var bag = expr.AllIn is { } allIn
            ? PortValues.Elements(Sub(allIn, scope, input, deps, field)).ToArray()
            : null;

        bool Match(object? item)
            => predicate is not null ? predicate(item)
               : bag is not null ? bag.Any(candidate => Equal(candidate, item))
               : item is not null;

        return every ? items.All(Match) : items.Any(Match);
    }

    /// <summary>量词判据：声明 <c>call</c> 时取静态谓词（每元素求布尔）；否则按 <c>allIn</c> / 非空判据处理。</summary>
    private static Func<object?, bool>? Predicate(ScriptedExpr expr, EvalScope scope)
    {
        if (expr.Call is not { Length: > 0 } name) return null;

        var target = expr.CallTarget is { Length: > 0 } typeName
            ? ComposedDeps.ResolveType(typeName, null)
            : throw new PortBindingViolationException(
                scope.PortKey, $"端口 '{scope.PortKey}' 的谓词 '{name}' 缺 callTarget（静态类型全名）");

        var method = ReflectivePortResolver.ResolveMethod(target, scope.PortKey, name, 1, isStatic: true);
        return item => PortInvocation.Execute(() => method.Invoke(null, new[] { item }), expect: null) is true;
    }

    private static IReadOnlyDictionary<string, object?> Spread(
        ScriptedExpr expr, ScriptedExpr source, EvalScope scope, IPortInput input, object?[] deps, string field)
    {
        var entries = PortValues.Spread(Sub(source, scope, input, deps, field));
        if (expr.Except is not { Count: > 0 } except) return entries;

        var kept = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
            if (!except.Contains(key, StringComparer.Ordinal)) kept[key] = value;

        return kept;
    }

    private static bool CountEqual(
        IReadOnlyList<ScriptedExpr> operands, EvalScope scope, IPortInput input, object?[] deps, string field)
        => Count(Sub(operands[0], scope, input, deps, field), scope.PortKey, field)
           == Count(Sub(operands[1], scope, input, deps, field), scope.PortKey, field);

    /// <summary>逻辑非（三值：<c>⊥</c>（null）→ <c>⊥</c>）。</summary>
    private static object? Not(object? value) => value is null ? null : !PortValues.Truthy(value);

    /// <summary>
    /// 逻辑合取 / 析取（三值：合取遇假即假、析取遇真即真；否则遇 <c>⊥</c> → <c>⊥</c>）。
    /// <para><c>⊥</c> 语义：调用方（步骤 <c>when</c>）按「不执行」处理（<c>Truthy(null)=false</c>），不静默成真。</para>
    /// </summary>
    private static object? Junction(
        IReadOnlyList<ScriptedExpr> parts, EvalScope scope, IPortInput input, object?[] deps,
        string field, bool every)
    {
        if (parts.Count == 0)
            throw new PortBindingViolationException(
                scope.PortKey,
                $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的 {(every ? "all" : "any")} 算子缺子表达式（不可空表）");

        var bottom = false;
        foreach (var part in parts)
        {
            var value = Sub(part, scope, input, deps, field);
            if (value is null)
            {
                bottom = true;
                continue;
            }

            if (PortValues.Truthy(value) != every) return !every;
        }

        return bottom ? null : (object)every;
    }

    /// <summary>
    /// 二元比较（三值：任一侧缺值 → <c>⊥</c>；<c>eq</c>/<c>ne</c> 按值相等，其余按数值序优先、回落文本序）。
    /// </summary>
    private static object? Compare(
        IReadOnlyList<ScriptedExpr> operands, EvalScope scope, IPortInput input, object?[] deps,
        string field, string op)
    {
        if (operands.Count != 2)
            throw new PortBindingViolationException(
                scope.PortKey,
                $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的 {op} 算子须恰两个操作数（当前 {operands.Count} 个）");

        var left = Sub(operands[0], scope, input, deps, field);
        var right = Sub(operands[1], scope, input, deps, field);

        if (left is null || right is null) return null;

        if (op is "eq" or "ne")
        {
            var same = Equal(left, right);
            return op == "eq" ? same : !same;
        }

        var order = PortValues.Number(left) is { } leftNumber && PortValues.Number(right) is { } rightNumber
            ? leftNumber.CompareTo(rightNumber)
            : string.CompareOrdinal(PortValues.Text(left), PortValues.Text(right));

        return op switch
        {
            "gt" => order > 0,
            "ge" => order >= 0,
            "lt" => order < 0,
            "le" => order <= 0,
            _ => throw new PortBindingViolationException(
                scope.PortKey,
                $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的比较算子 '{op}' 未知（eq|ne|gt|ge|lt|le）"),
        };
    }
}
