namespace Strategy.Observability.Ports;

using System.Reflection;
using System.Text.Json;
using Strategy.Observability.Diagnostics;


/// <summary>
/// 投影表达式求值器（E22 自证对象）：源值 → 算子递归求值；未知形态 / 悬空引用 → fail-fast。
/// <para>算子语义（规范 §5）：<c>count</c> 计数、<c>notNull</c> 非空、<c>text</c> 文本、<c>typeName</c>/<c>message</c>/<c>thrown</c>
/// 异常三态、<c>concat</c> 拼接、<c>contains</c> 包含、<c>all</c>/<c>any</c> 量词、<c>spread</c> 动态键展开、
/// <c>countEqual</c> 取等、<c>collect</c> 收集槽逐项投影。</para>
/// </summary>
internal static class ScriptedExprEvaluator
{
    /// <summary>求值（<paramref name="field"/> 仅用于诊断定位）。</summary>
    internal static object? Evaluate(
        ScriptedExpr expr, EvalScope scope, IPortInput input, object?[] deps, string field)
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

    private static object? First(
        ScriptedExpr expr, ScriptedExpr over, EvalScope scope, IPortInput input, object?[] deps,
        string field, bool orNull)
    {
        var items = PortValues.Elements(Sub(over, scope, input, deps, field)).ToArray();
        var index = -1;

        foreach (var item in items)
        {
            index++;
            scope.Push(EvalScope.RowFrame, item, index);

            bool hit;
            try
            {
                hit = expr.FirstWhere is { } predicate
                    ? PortValues.Truthy(Sub(predicate, scope, input, deps, field))
                    : item is not null;
            }
            finally
            {
                scope.Pop();
            }

            if (!hit) continue;

            return expr.FirstPath is { Length: > 0 } path ? PortValues.PathOf(item, path) : item;
        }

        if (orNull) return null;

        throw new PortBindingViolationException(
            scope.PortKey,
            $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的 first 未命中（{items.Length} 个元素无匹配）");
    }

    /// <summary>
    /// 末命中取值（W15）：自尾端起首个命中项（判据同 <c>first</c>；空集 / 无命中 → <c>null</c>）。
    /// 用于夹具 <c>FirstOrDefault</c> / <c>LastOrDefault</c> / <c>SingleOrDefault</c> 一类「缺省即空」取值。
    /// </summary>
    private static object? Last(
        ScriptedExpr expr, ScriptedExpr over, EvalScope scope, IPortInput input, object?[] deps,
        string field)
    {
        var items = PortValues.Elements(Sub(over, scope, input, deps, field)).ToArray();

        for (var index = items.Length - 1; index >= 0; index--)
        {
            scope.Push(EvalScope.RowFrame, items[index], index);

            bool hit;
            try
            {
                hit = expr.LastWhere is { } predicate
                    ? PortValues.Truthy(Sub(predicate, scope, input, deps, field))
                    : items[index] is not null;
            }
            finally
            {
                scope.Pop();
            }

            if (!hit) continue;

            return expr.LastPath is { Length: > 0 } path ? PortValues.PathOf(items[index], path) : items[index];
        }

        // 末命中语义：空集 / 无命中一律取 null（不抛；LastOrNull 为声明自描述字段）。
        return null;
    }

    /// <summary>
    /// 键值表装配（W14）：遍历来源元素 → 逐项取键（<c>key</c>）与值（<c>value</c>）→ 字典。
    /// <para>口径：<c>where</c> 求值为假即跳过该项；键文本为空 → 跳过（不可作键，同 <c>presence</c>）；键重复 → 后者覆盖
    /// （夹具 <c>map[k]=v</c> 口径）；空集 = 空字典（不抛）。</para>
    /// </summary>
    private static object? Dict(
        ScriptedExpr expr, ScriptedExpr over, EvalScope scope, IPortInput input, object?[] deps, string field)
    {
        var keyExpr = expr.DictKey ?? throw new PortBindingViolationException(
            scope.PortKey, $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的 dict 缺 key（表键表达式）");

        var items = PortValues.Elements(Sub(over, scope, input, deps, field)).ToArray();
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);
        var overwrite = string.Equals(expr.DictOnDuplicate, "last", StringComparison.Ordinal);

        for (var index = 0; index < items.Length; index++)
        {
            scope.Push(EvalScope.RowFrame, items[index], index);
            try
            {
                if (expr.DictWhere is { } gate
                    && !PortValues.Truthy(Sub(gate, scope, input, deps, field)))
                {
                    continue;
                }

                if (PortValues.Text(Sub(keyExpr, scope, input, deps, field)) is not { Length: > 0 } key) continue;

                var value = expr.DictValue is { } valueExpr
                    ? Sub(valueExpr, scope, input, deps, field)
                    : items[index];

                if (overwrite || !map.ContainsKey(key))
                {
                    map[key] = value;
                    continue;
                }

                throw new PortBindingViolationException(
                    scope.PortKey,
                    $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的 dict 键 '{key}' 重复（第 {index + 1} 项；"
                    + "夹具 TryAdd 口径 fail-fast；需覆盖请声明 onDuplicate:\"last\"）");
            }
            finally
            {
                scope.Pop();
            }
        }

        return map;
    }

    /// <summary>
    /// 聚合取值（W3）：对来源元素逐项取 <see cref="ScriptedExpr.AggPath"/> → 数值化 → 按 <c>sum|max|min|avg</c> 聚合。
    /// <para>口径：非数值元素即抛（不静默跳过）；<c>sum</c> 空集 = 0，<c>max|min|avg</c> 空集即抛；
    /// 全整数输入且结果可容纳于 <see cref="int"/> 时按整数返回（与夹具的整数期望可比）。</para>
    /// </summary>
    private static object? Aggregate(
        ScriptedExpr expr, ScriptedExpr over, EvalScope scope, IPortInput input, object?[] deps, string field)
    {
        var op = expr.AggOp ?? "sum";
        var items = PortValues.Elements(Sub(over, scope, input, deps, field)).ToArray();
        var numbers = new List<double>(items.Length);

        foreach (var item in items)
        {
            var value = expr.AggPath is { Length: > 0 } path ? PortValues.PathOf(item, path) : item;
            if (value is null) continue;

            if (PortValues.Number(value) is not { } number)
            {
                // 集合元素按**元素计数**参与聚合（W29：Sum(v => v.Items.Count) 的声明等价）：
                // 计数经 PortValues 单源（含 JSON 数组 / JSON 对象，E7）；**字符串不回退**（保持「非数值即抛」，防把长度当数值）。
                if (value is not string && PortValues.TryCount(value) is { } size)
                {
                    numbers.Add(size);
                    continue;
                }

                throw new PortBindingViolationException(
                    scope.PortKey,
                    $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的 agg 元素 '{PortValues.Text(value)}' 非数值");
            }

            numbers.Add((double)number);
        }

        var integral = numbers.All(number => Math.Abs(number % 1) < double.Epsilon);
        var total = numbers.Sum();

        return op switch
        {
            "sum" => integral && total is >= int.MinValue and <= int.MaxValue ? (int)total : (object)total,
            "max" when numbers.Count > 0 => Extreme(numbers.Max(), integral),
            "min" when numbers.Count > 0 => Extreme(numbers.Min(), integral),
            "avg" when numbers.Count > 0 => numbers.Average(),
            "max" or "min" or "avg" => throw new PortBindingViolationException(
                scope.PortKey, $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的 agg.{op} 无可取值元素"),
            _ => throw new PortBindingViolationException(
                scope.PortKey, $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的 agg 算子 '{op}' 未知（sum|max|min|avg）"),
        };
    }

    /// <summary>
    /// 聚合取值结果归一：整数输入按 <see cref="int"/> 返回（可比性），否则原样 <see cref="double"/>。
    /// <para>E6 修复点：三元两支须显式为 <c>object</c>（否则 <c>int</c> 被隐式提升回 <c>double</c> 再装箱，整数归一无效果）。</para>
    /// </summary>
    private static object Extreme(double value, bool integral)
        => integral && value is >= int.MinValue and <= int.MaxValue ? (int)value : (object)value;

    /// <summary>
    /// 筛选成集（W8）：遍历来源元素 → 按 <see cref="ScriptedExpr.FilterWhere"/> 保留（真值判定；<c>⊥</c> 视同不保留）
    /// → 逐项取 <see cref="ScriptedExpr.FilterPath"/> → 数组。空集 = 空数组（不抛）。
    /// </summary>
    private static object?[] Filter(
        ScriptedExpr expr, ScriptedExpr over, EvalScope scope, IPortInput input, object?[] deps, string field)
    {
        var items = PortValues.Elements(Sub(over, scope, input, deps, field)).ToArray();
        var kept = new List<object?>(items.Length);
        var index = -1;

        foreach (var item in items)
        {
            index++;
            scope.Push(EvalScope.RowFrame, item, index);

            bool hit;
            try
            {
                hit = expr.FilterWhere is { } predicate
                    ? PortValues.Truthy(Sub(predicate, scope, input, deps, field))
                    : item is not null;
            }
            finally
            {
                scope.Pop();
            }

            if (!hit) continue;
            kept.Add(expr.FilterPath is { Length: > 0 } path ? PortValues.PathOf(item, path) : item);
        }

        return kept.ToArray();
    }

    /// <summary>引用同一性（W5）：<c>ReferenceEquals(左, 右)</c>（值相等但非同一引用 → false）。</summary>
    private static bool SameReference(
        IReadOnlyList<ScriptedExpr> operands, EvalScope scope, IPortInput input, object?[] deps, string field)
        => ReferenceEquals(
            Sub(operands[0], scope, input, deps, field),
            Sub(operands[1], scope, input, deps, field));

    private static object? Coalesce(
        IReadOnlyList<ScriptedExpr> operands, EvalScope scope, IPortInput input, object?[] deps, string field)
        => Sub(operands[0], scope, input, deps, field)
           ?? Sub(operands[1], scope, input, deps, field);

    private static object?[] Collect(
        ScriptedExpr expr, string name, EvalScope scope, IPortInput input, object?[] deps, string field)
    {
        var items = PortValues.Elements(scope.Slot(name, $"投影字段 '{field}' 的 collect")).ToArray();

        var projected = items
            .Select(item => expr.Path is { Length: > 0 } path ? PortValues.PathOf(item, path) : item)
            .ToArray();

        if (expr.SameAsLast is { Length: > 0 } other)
        {
            var last = PortValues.Elements(
                scope.Slot(other, $"投影字段 '{field}' 的 sameAsLast")).LastOrDefault();

            return projected.Select(item => (object?)ReferenceEquals(item, last)).ToArray();
        }

        return expr.CollectText
            ? projected.Select(item => (object?)PortValues.Text(item)).ToArray()
            : projected;
    }

    /// <summary>值等价（JSON 元素按原义归一；数值跨 CLR 类型比较；字符串按序号比较）。</summary>
    private static bool Equal(object? left, object? right)
    {
        var a = Normalize(left);
        var b = Normalize(right);

        if (a is string text && b is string other)
            return string.Equals(text, other, StringComparison.Ordinal);

        if (a is not string && b is not string
            && PortValues.Number(a) is { } leftNumber && PortValues.Number(b) is { } rightNumber)
            return leftNumber == rightNumber;

        return Equals(a, b);
    }

    /// <summary>JSON 标量 → CLR 原义值（结构保留元素，供路径 / 计数 / 展开使用）。</summary>
    private static object? Normalize(object? value)
        => value is JsonElement element && element.ValueKind is not (JsonValueKind.Object or JsonValueKind.Array)
            ? PortValues.Unwrap(element)
            : value;
}
