namespace Strategy.Observability.Ports;

using System.Reflection;
using System.Text.Json;
using Strategy.Observability.Diagnostics;


internal static partial class ScriptedExprEvaluator
{
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
