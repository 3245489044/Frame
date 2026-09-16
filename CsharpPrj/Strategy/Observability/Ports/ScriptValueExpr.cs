namespace Strategy.Observability.Ports;

using System.Text.Json;
using Strategy.Observability.Diagnostics;

/// <summary>
/// 投影**取值表达式**（取代固定取值法枚举；递归可组合）：<see cref="Source"/> 取源值，
/// 其余成员为算子（每次恰好一个）。语义表见《AgentTest 端口声明式编排框架-设计规范》§5。
/// <para>分工：本件为机制件——表达式成员是纯结构算子（计数 / 拼接 / 包含 / 全称 / 存在 / 展开 / 相等），
/// 不含任何领域谓词；<c>call</c> 谓词的语义由声明里的静态方法名承载（不透明字符串）。</para>
/// </summary>
/// <param name="Source">源值（槽 / 实参 / 遍历变量 / 依赖 / 常量；可带属性路径）。</param>
/// <param name="Path">属性路径（与 <see cref="Collect"/> 连用：对收集元素逐项取路径）。</param>
/// <param name="Count">计数（集合 / 数组 / 字符串；null → 0）。</param>
/// <param name="NotNull">非空布尔。</param>
/// <param name="AsText">文本形式。</param>
/// <param name="TypeName">运行时类型名（异常 / <see cref="Type"/> / 一般值）。</param>
/// <param name="Message">异常消息。</param>
/// <param name="Thrown">是否为异常对象（期望异常命中）。</param>
/// <param name="Concat">片段按序拼接（null → 空串）。</param>
/// <param name="Separator">拼接分隔符（缺省空串）。</param>
/// <param name="Contains">被查集合 / 文本。</param>
/// <param name="Of">被含元素 / 子串 / 字典键。</param>
/// <param name="All">全称判定：遍历该表达式的元素。</param>
/// <param name="Any">存在判定：遍历该表达式的元素。</param>
/// <param name="AllIn">全称 / 存在的判据：元素 ∈ 该集合（与 <see cref="Call"/> 二选一）。</param>
/// <param name="Call">判据谓词方法名（静态；对每个元素求值取布尔）。</param>
/// <param name="CallTarget">判据谓词的静态类型全名。</param>
/// <param name="Spread">动态键展开（对象属性 / 字典逐项写入产物）。</param>
/// <param name="Except">展开时排除的键。</param>
/// <param name="CountEqual">两次取值计数相等（恰两个操作数）。</param>
/// <param name="Collect">收集槽名（对收集元素逐项投影）。</param>
/// <param name="CollectText">收集元素逐项取文本。</param>
/// <param name="SameAsLast">收集元素与另一收集槽末位引用相等（逐项布尔）。</param>
public sealed record ScriptedExpr(
    ScriptedArgSpec? Source = null,
    string? Path = null,
    ScriptedExpr? Count = null,
    ScriptedExpr? NotNull = null,
    ScriptedExpr? AsText = null,
    ScriptedExpr? TypeName = null,
    ScriptedExpr? Message = null,
    ScriptedExpr? Thrown = null,
    IReadOnlyList<ScriptedExpr>? Concat = null,
    string? Separator = null,
    ScriptedExpr? Contains = null,
    ScriptedExpr? Of = null,
    ScriptedExpr? All = null,
    ScriptedExpr? Any = null,
    ScriptedExpr? AllIn = null,
    string? Call = null,
    string? CallTarget = null,
    ScriptedExpr? Spread = null,
    IReadOnlyList<string>? Except = null,
    IReadOnlyList<ScriptedExpr>? CountEqual = null,
    string? Collect = null,
    bool CollectText = false,
    string? SameAsLast = null);

/// <summary>产物字段 ← 投影表达式（字段序 = 产物序；字段名须与契约声明逐字同序）。</summary>
/// <param name="Field">产物字段名。</param>
/// <param name="Expr">取值表达式。</param>
public sealed record ScriptedFieldSpec(string Field, ScriptedExpr Expr);

/// <summary>
/// 载体开关（L0 载体的声明面表达）：<c>defer</c> = 产物包装为延迟动作（断言期求值，供 <c>throws</c>）；
/// <c>issues</c> = 产物包装为问题清单（供 <c>issueCount</c> / <c>issueContains</c>）。
/// </summary>
/// <param name="Defer">延迟动作载体。</param>
/// <param name="Issues">问题清单载体。</param>
/// <param name="IssueField">清单来源字段 / 收集槽名（空 = 取投影首字段）。</param>
public sealed record ScriptedDeliverSpec(
    bool Defer = false,
    bool Issues = false,
    string? IssueField = null);


/// <summary>
/// 脚本求值作用域：具名槽表 + 循环变量栈（<c>foreach</c> 内步骤可见当前元素 / 下标 / 元素属性）。
/// <para>槽写入唯一入口 = <see cref="Set"/>；循环变量随迭代入栈出栈（嵌套遍历按最内层优先解析）。</para>
/// </summary>
internal sealed class ScriptedScope
{
    private readonly Dictionary<string, object?> _slots = new(StringComparer.Ordinal);
    private readonly List<(string Name, object? Element, int Index)> _loops = new();

    internal ScriptedScope(string portKey) => PortKey = portKey;

    /// <summary>端口键（诊断定位用）。</summary>
    internal string PortKey { get; }

    /// <summary>写入槽（已存在的槽可被后续步骤覆盖：声明序即执行序）。</summary>
    internal void Set(string name, object? value) => _slots[name] = value;

    /// <summary>读槽（未定义 → fail-fast，不静默给 null）。</summary>
    internal object? Slot(string name, string context)
        => _slots.TryGetValue(name, out var value)
            ? value
            : throw new PortBindingViolationException(
                PortKey, $"端口 '{PortKey}' 的 {context} 引用了未定义的槽 '{name}'");

    internal bool Has(string name) => _slots.ContainsKey(name);

    /// <summary>收集槽（不存在则建）：循环结果按序追加（G1）。</summary>
    internal List<object?> Collect(string name)
    {
        if (_slots.TryGetValue(name, out var existing) && existing is List<object?> list) return list;

        var created = new List<object?>();
        _slots[name] = created;
        return created;
    }

    /// <summary>入栈循环变量（返回后须配 <see cref="Pop"/>）。</summary>
    internal void Push(string name, object? element, int index) => _loops.Add((name, element, index));

    /// <summary>出栈循环变量。</summary>
    internal void Pop() => _loops.RemoveAt(_loops.Count - 1);

    /// <summary>遍历变量解析：元素本身 / 元素下标 / 元素属性路径（未在遍历中 → fail-fast）。</summary>
    internal object? Item(string? name, int? index, string? path, string context)
    {
        var frame = _loops.LastOrDefault(loop => name is null || string.Equals(loop.Name, name, StringComparison.Ordinal));
        if (frame.Name is null)
            throw new PortBindingViolationException(
                PortKey, $"端口 '{PortKey}' 的 {context} 引用了未在遍历中的变量 '{name}'");

        var value = index is { } position ? ElementAt(frame.Element, position, context) : frame.Element;
        return path is { Length: > 0 } ? ScriptedValue.PathOf(value, path) : value;
    }

    /// <summary>元素下标取值（列表 / 数组 / 字符串；越界 / 非序载体 → fail-fast）。</summary>
    private object? ElementAt(object? element, int position, string context)
    {
        switch (element)
        {
            case null:
                return null;
            case System.Collections.IList list when position >= 0 && position < list.Count:
                return list[position];
            case string text when position >= 0 && position < text.Length:
                return text[position].ToString();
            default:
                throw new PortBindingViolationException(
                    PortKey,
                    $"端口 '{PortKey}' 的 {context} 对 {element?.GetType().Name ?? "null"} 取下标 {position} 越界");
        }
    }
}

/// <summary>
/// 脚本取值单源（<c>bind</c> / 步骤实参 / 表达式源共用）：来源解析 + 目标类型转换 + fail-fast 诊断。
/// <para>分工：脚本家族的来源词汇含遍历变量与槽引用（组合绑定的三种来源不含），故与
/// <see cref="ComposedArgs"/> 并列而不同源；值变换（路径 / 计数 / JSON 归一）单源 = <see cref="PortValues"/>。</para>
/// </summary>
internal static class ScriptedValue
{
    /// <summary>按来源取值（不做类型转换；缺来源 / 未定义槽 / 越界依赖 → fail-fast）。</summary>
    internal static object? Resolve(
        ScriptedArgSpec? spec, ScriptedScope scope, IPortInput input, object?[] deps, string context)
    {
        if (spec is null)
            throw new PortBindingViolationException(
                scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 未声明取值来源（from）");

        switch (spec.From)
        {
            case "arg":
                return Positional(spec, input, scope.PortKey, context);

            case "with":
                return With(spec, input, scope.PortKey, context);

            case "item":
                return scope.Item(spec.Key, spec.Index, spec.Path, context);

            case "var":
                if (string.IsNullOrWhiteSpace(spec.Key))
                    throw new PortBindingViolationException(
                        scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 的 var 来源缺 key（槽名）");

                var slot = scope.Slot(spec.Key!, context);
                return spec.Path is { Length: > 0 } path ? PortValues.PathOf(slot, path) : slot;

            case "dep":
                return spec.Dep is { } index
                    ? ComposedDeps.Dep(deps, index, index, scope.PortKey)
                    : throw new PortBindingViolationException(
                        scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 的 dep 来源缺 dep（依赖下标）");

            case "literal":
                return spec.Value is { } literal ? PortValues.Unwrap(literal) : null;

            case null or "" when spec.Optional:
                return null;

            default:
                throw new PortBindingViolationException(
                    scope.PortKey,
                    $"端口 '{scope.PortKey}' 的 {context} 取值来源 '{spec.From}' 未知" +
                    "（arg|with|item|var|dep|literal）");
        }
    }

    /// <summary>按目标类型取值（声明 <c>type</c> 优先，否则用调用方给的形参类型；无声明 → 目标类型默认值）。</summary>
    internal static object? ResolveTyped(
        ScriptedArgSpec? spec, Type target, ScriptedScope scope, IPortInput input, object?[] deps, string context)
        => spec is null
            ? ReflectivePortResolver.DefaultFor(target)
            : Convert(
                Resolve(spec, scope, input, deps, context),
                spec,
                DeclaredType(spec) ?? target,
                scope.PortKey,
                context);

    /// <summary>声明目标类型（<c>type</c> + <c>genericArgs</c>；未声明 → null）。</summary>
    internal static Type? DeclaredType(ScriptedArgSpec spec)
        => spec.Type is { Length: > 0 } name ? ComposedDeps.ResolveType(name, spec.GenericArgs) : null;

    /// <summary>属性路径取值（单源转调 <see cref="PortValues.PathOf"/>）。</summary>
    internal static object? PathOf(object? value, string path) => PortValues.PathOf(value, path);

    /// <summary>按 <c>convert</c> 与目标形参类型转换（无 <c>convert</c>：JSON 反序列化 / 原类型直投）。</summary>
    private static object? Convert(object? raw, ScriptedArgSpec spec, Type target, string portKey, string context)
    {
        if (raw is null)
        {
            if (spec.Default is { } fallback) return PortValues.Coerce(fallback, target);
            if (spec.Optional) return ReflectivePortResolver.DefaultFor(target);
            return null;
        }

        switch (spec.Convert)
        {
            case "string":
                return PortValues.Text(raw);

            case "csv":
                return Csv(PortValues.Text(raw) ?? string.Empty, target, portKey, context);

            case "graph":
                return raw is JsonElement graph ? PortValues.ToGraph(graph) : raw;

            case "json":
                return PortValues.Coerce(
                    raw is JsonElement element
                        ? element
                        : JsonDocument.Parse(PortValues.Text(raw) ?? "null").RootElement,
                    target);

            case "bool":
                return bool.TryParse(PortValues.Text(raw), out var flag)
                    ? flag
                    : throw Mismatch(portKey, context, raw, "布尔");

            case "int":
                return int.TryParse(PortValues.Text(raw), System.Globalization.NumberStyles.Integer,
                    System.Globalization.CultureInfo.InvariantCulture, out var number)
                    ? number
                    : throw Mismatch(portKey, context, raw, "整数");

            case "double":
                return double.TryParse(PortValues.Text(raw), System.Globalization.NumberStyles.Float,
                    System.Globalization.CultureInfo.InvariantCulture, out var real)
                    ? real
                    : throw Mismatch(portKey, context, raw, "数值");

            case null or "":
                return Direct(raw, target, portKey, context);

            default:
                throw new PortBindingViolationException(
                    portKey,
                    $"端口 '{portKey}' 的 {context} 的 convert '{spec.Convert}' 未知" +
                    "（string|bool|int|double|csv|json）");
        }
    }

    private static PortBindingViolationException Mismatch(string portKey, string context, object raw, string expected)
        => new(portKey, $"端口 '{portKey}' 的 {context} 的值 '{PortValues.Text(raw)}' 不能转换为 {expected}");

    /// <summary>无转换声明：JSON 值按目标类型反序列化；原类型直投；值类型按文本宽松解析。</summary>
    private static object? Direct(object raw, Type target, string portKey, string context)
    {
        if (raw is JsonElement) return PortValues.Coerce(raw, target);
        if (target == typeof(object) || target.IsInstanceOfType(raw)) return raw;

        if (target.IsValueType && raw is string text)
        {
            try
            {
                return System.Convert.ChangeType(text, target, System.Globalization.CultureInfo.InvariantCulture);
            }
            catch (Exception ex) when (ex is FormatException or InvalidCastException or OverflowException)
            {
                throw Mismatch(portKey, context, raw, target.Name);
            }
        }

        throw Mismatch(portKey, context, raw, target.Name);
    }

    /// <summary>CSV 文本 → 目标集合类型（逗号拆分；数组 / 字符串集合均可）。</summary>
    private static object? Csv(string text, Type target, string portKey, string context)
    {
        var parts = text.Length == 0
            ? new List<string?>()
            : text.Split(',').Select(item => (string?)item.Trim()).ToList();

        if (target.IsArray) return parts.ToArray();
        if (target.IsAssignableFrom(parts.GetType())) return parts;
        if (target == typeof(object)) return parts;

        throw Mismatch(portKey, context, text, target.Name);
    }

    /// <summary>位置实参取值（越界 → fail-fast；文本按 <c>convert</c> 原义取用）。</summary>
    private static object? Positional(ScriptedArgSpec spec, IPortInput input, string portKey, string context)
    {
        var position = spec.Index
            ?? throw new PortBindingViolationException(
                portKey, $"端口 '{portKey}' 的 {context} 的 arg 来源缺 index（位置实参下标）");

        if (position >= input.Args.Count)
        {
            if (spec.Optional) return null;

            throw new PortBindingViolationException(
                portKey, $"端口 '{portKey}' 的 {context} 需要第 {position} 个位置实参，实际只有 {input.Args.Count} 个");
        }

        var text = input.Args[position] as string ?? input.Args[position]?.ToString() ?? string.Empty;
        if (spec.NullLiteral && string.Equals(text, "null", StringComparison.Ordinal)) return null;

        return text;
    }

    /// <summary>具名实参取值（原始 JSON；缺键 → <c>optional</c> 决定 null 或 fail-fast）。</summary>
    private static object? With(ScriptedArgSpec spec, IPortInput input, string portKey, string context)
    {
        if (string.IsNullOrWhiteSpace(spec.Key))
            throw new PortBindingViolationException(
                portKey, $"端口 '{portKey}' 的 {context} 的 with 来源缺 key（具名实参键）");

        if (input.With.TryGetValue(spec.Key!, out var value)) return value;
        if (spec.Optional) return null;

        throw new PortBindingViolationException(
            portKey, $"端口 '{portKey}' 的 {context} 缺具名实参 '{spec.Key}'（with）");
    }
}

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
        ScriptedExpr expr, ScriptedScope scope, IPortInput input, object?[] deps, string field)
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
        if (expr.Collect is { Length: > 0 } collect) return Collect(expr, collect, scope, input, deps, field);

        throw new PortBindingViolationException(
            scope.PortKey, $"端口 '{scope.PortKey}' 的投影字段 '{field}' 的表达式未声明来源或算子");
    }

    private static object? Sub(
        ScriptedExpr expr, ScriptedScope scope, IPortInput input, object?[] deps, string field)
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
        ScriptedScope scope, IPortInput input, object?[] deps, string field)
        => string.Join(expr.Separator ?? string.Empty,
            parts.Select(part => PortValues.Text(Sub(part, scope, input, deps, field)) ?? string.Empty));

    private static bool Contains(
        ScriptedExpr expr, ScriptedExpr container, ScriptedScope scope, IPortInput input, object?[] deps, string field)
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
        ScriptedExpr expr, ScriptedExpr over, ScriptedScope scope, IPortInput input, object?[] deps,
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
    private static Func<object?, bool>? Predicate(ScriptedExpr expr, ScriptedScope scope)
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
        ScriptedExpr expr, ScriptedExpr source, ScriptedScope scope, IPortInput input, object?[] deps, string field)
    {
        var entries = PortValues.Spread(Sub(source, scope, input, deps, field));
        if (expr.Except is not { Count: > 0 } except) return entries;

        var kept = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var (key, value) in entries)
            if (!except.Contains(key, StringComparer.Ordinal)) kept[key] = value;

        return kept;
    }

    private static bool CountEqual(
        IReadOnlyList<ScriptedExpr> operands, ScriptedScope scope, IPortInput input, object?[] deps, string field)
        => Count(Sub(operands[0], scope, input, deps, field), scope.PortKey, field)
           == Count(Sub(operands[1], scope, input, deps, field), scope.PortKey, field);

    private static object?[] Collect(
        ScriptedExpr expr, string name, ScriptedScope scope, IPortInput input, object?[] deps, string field)
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

    /// <summary>值等价（字符串按序号比较；其余按 <c>Equals</c>）。</summary>
    private static bool Equal(object? left, object? right)
        => left is string a && right is string b
            ? string.Equals(a, b, StringComparison.Ordinal)
            : Equals(left, right);
}


