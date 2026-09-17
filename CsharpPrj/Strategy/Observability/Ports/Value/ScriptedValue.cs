namespace Strategy.Observability.Ports;

using System.Reflection;
using System.Text.Json;
using DataCenter.Protocol.Standards;
using Strategy.Observability.Diagnostics;


/// <summary>
/// 脚本取值单源（<c>bind</c> / 步骤实参 / 表达式源共用）：来源解析 + 目标类型转换 + fail-fast 诊断。
/// <para>分工：脚本家族的来源词汇含遍历变量与槽引用（组合绑定的三种来源不含），故与
/// <see cref="ComposedArgs"/> 并列而不同源；值变换（路径 / 计数 / JSON 归一）单源 = <see cref="PortValues"/>。</para>
/// </summary>
internal static class ScriptedValue
{
    /// <summary>按来源取值（不做类型转换；缺来源 / 未定义槽 / 越界依赖 → fail-fast）。</summary>
    internal static object? Resolve(
        ScriptedArgSpec? spec, EvalScope scope, IPortInput input, object?[] deps, string context)
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

            case "row":
                return scope.Item(EvalScope.RowFrame, spec.Index, spec.Path, context);

            case "table":
                return Table(spec, scope, input, deps, context);

            case "presence":
                return Presence(spec, scope, input, deps, context);

            case "tmpDir":
                return scope.TempDir(spec.Scope, context);

            case "tmpPath":
                return TempFile(spec, scope, context);

            case "var":
                if (string.IsNullOrWhiteSpace(spec.Key))
                    throw new PortBindingViolationException(
                        scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 的 var 来源缺 key（槽名）");

                // E8：运行期未写入的槽 → ⊥（不抛；声明期拼写漂移仍由机检拦）。
                var slot = scope.SlotOrBottom(spec.Key!, context);
                return spec.Path is { Length: > 0 } path ? PortValues.PathOf(slot, path) : slot;

            case "dep":
                return spec.Dep is { } index
                    ? ComposedDeps.Dep(deps, index, index, scope.PortKey)
                    : throw new PortBindingViolationException(
                        scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 的 dep 来源缺 dep（依赖下标）");

            case "literal":
                return spec.Value is { } literal ? PortValues.Unwrap(literal) : null;

            case "member":
                return StaticMember(spec, scope.PortKey, context);

            case "type":
                return TypeLiteral(spec, scope.PortKey, context);

            case "delegate":
                return DelegateValue(spec, scope, input, deps, context);

            case null or "" when spec.Construct is not null:
                return ScriptedConstruction.Build(spec.Construct!, scope, input, deps, context);

            // E5：From 缺省按**声明内容**推断（优先级固定唯一）——Construct > Delegate > Value；
            // 三者皆空 → 保留既有 fail-fast（列可用来源）。
            case null or "" when spec.Delegate is not null:
                return DelegateValue(spec, scope, input, deps, context);

            case null or "" when spec.Value is { } constant:
                return PortValues.Unwrap(constant);

            case "new":
                return spec.Construct is { } construct
                    ? ScriptedConstruction.Build(construct, scope, input, deps, context)
                    : throw new PortBindingViolationException(
                        scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 的 new 来源缺 construct（构造声明）");

            case null or "" when spec.Optional:
                return null;

            default:
                throw new PortBindingViolationException(
                    scope.PortKey,
                    $"端口 '{scope.PortKey}' 的 {context} 取值来源 '{spec.From}' 未知" +
                    "（arg|with|item|row|var|dep|literal|member|new|type|delegate|table|presence|tmpPath|tmpDir）");
        }
    }

    /// <summary>按目标类型取值（声明 <c>type</c> 优先，否则用调用方给的形参类型；无声明 → 目标类型默认值）。</summary>
    internal static object? ResolveTyped(
        ScriptedArgSpec? spec, Type target, EvalScope scope, IPortInput input, object?[] deps, string context)
        => spec is null
            ? ReflectivePortResolver.DefaultFor(target)
            : Convert(
                Resolve(spec, scope, input, deps, context),
                spec,
                DeclaredType(spec) ?? target,
                scope.PortKey,
                context);

    /// <summary>声明目标类型（<c>type</c> + <c>genericArgs</c>；未声明 → null）。<c>from:type</c> 时
    /// <c>type</c> 是**取值本身**（类型字面量）而非目标类型，故不参与目标类型解析。</summary>
    internal static Type? DeclaredType(ScriptedArgSpec spec)
        => spec.From == "type"
            ? null
            : spec.Type is { Length: > 0 } name ? ComposedDeps.ResolveType(name, spec.GenericArgs) : null;

    /// <summary>声明实参摘要（诊断文案：重载择优的「实参 […]」段）。</summary>
    internal static string DescribeArgs(IReadOnlyList<ScriptedArgSpec>? declared)
        => declared is not { Count: > 0 } ? "(无)" : string.Join(", ", declared.Select(DescribeArg));

    private static string DescribeArg(ScriptedArgSpec spec)
        => spec.From switch
        {
            "literal" when spec.Value is { } value => $"literal {value.GetRawText()}",
            "arg" => $"arg[{spec.Index?.ToString() ?? "?"}]",
            "with" => $"with '{spec.Key}'",
            "var" => $"var '{spec.Key}'",
            "item" or "row" => $"{spec.From} '{spec.Key}'",
            "dep" => $"dep[{spec.Dep?.ToString() ?? "?"}]",
            "member" => $"member {spec.Type}.{spec.Key}",
            "type" => $"type {spec.Type}",
            "new" => $"new {spec.Construct?.Type}",
            "delegate" => $"delegate {spec.Delegate?.Kind}",
            null or "" when spec.Construct is not null => $"new {spec.Construct.Type}",
            null or "" when spec.Delegate is not null => $"delegate {spec.Delegate.Kind}",
            null or "" when spec.Value is not null => $"literal {spec.Value.Value.GetRawText()}",
            _ => spec.From ?? "(未声明来源)",
        };

    /// <summary>委托来源取值（<c>from:delegate</c> 与 E5 的 From 缺省推断共用）：委托工厂未接线 / 缺声明 → fail-fast。</summary>
    private static object? DelegateValue(
        ScriptedArgSpec spec, EvalScope scope, IPortInput input, object?[] deps, string context)
        => spec.Delegate is { } delegateSpec
            ? (scope.Delegates ?? throw new PortBindingViolationException(
                scope.PortKey,
                $"端口 '{scope.PortKey}' 的 {context} 的 delegate 来源未接线（委托工厂未注入）"))
              (delegateSpec, scope, input, deps, context)
            : throw new PortBindingViolationException(
                scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 的 delegate 来源缺 delegate（委托声明）");

    /// <summary>属性路径取值（单源转调 <see cref="PortValues.PathOf"/>）。</summary>
    internal static object? PathOf(object? value, string path) => PortValues.PathOf(value, path);

    /// <summary>作用域内文件绝对路径（<c>name</c> 不得含路径分隔符：子目录由 <c>dir</c> 表达式声明）。</summary>
    private static string TempFile(ScriptedArgSpec spec, EvalScope scope, string context)
    {
        if (spec.Name is not { Length: > 0 } name)
            throw new PortBindingViolationException(
                scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 的 tmpPath 来源缺 name（文件名）");

        if (name.IndexOfAny(['\\', '/', ':']) >= 0)
            throw new PortBindingViolationException(
                scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 的文件名 '{name}' 不得含路径分隔符（子目录用 dir 声明）");

        return System.IO.Path.Combine(scope.TempDir(spec.Scope, context), name);
    }

    /// <summary>
    /// 行表装配（P3）：行集 → 键值表（空首列 / 重复键的处置与 L0 <c>SpecTables.Rows</c> 同口径）。
    /// <para>键 = 首列（<c>separator</c> 非空时按分隔符拼接全部列）；值 = <c>rowValue</c> 表达式
    /// （体内 <c>from:row</c> 取列），缺省 = 首列文本。逐行以行帧入栈（行内可再引 <c>item</c> / <c>var</c>）。</para>
    /// </summary>
    private static Dictionary<string, object?> Table(
        ScriptedArgSpec spec, EvalScope scope, IPortInput input, object?[] deps, string context)
    {
        var source = spec.Rows ?? throw new PortBindingViolationException(
            scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 的 table 来源缺 rows（行集来源）");

        var rows = PortValues.Elements(Resolve(source, scope, input, deps, $"{context} 的行集")).ToArray();
        var separator = spec.Separator ?? "|";
        var onBlank = spec.OnBlankFirstKey ?? "fail";
        var onDuplicate = spec.OnDuplicate ?? "fail";
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);

        for (var index = 0; index < rows.Length; index++)
        {
            scope.Push(EvalScope.RowFrame, rows[index], index);
            try
            {
                if (spec.Filter is { } filter
                    && !PortValues.Truthy(ScriptedExprEvaluator.Evaluate(
                        filter, scope, input, deps, $"{context} 的行过滤")))
                {
                    continue;
                }

                var cells = PortValues.Elements(rows[index]).ToArray();
                var head = cells.Length > 0 ? PortValues.Text(cells[0]) : null;
                if (string.IsNullOrWhiteSpace(head))
                {
                    if (string.Equals(onBlank, "skip", StringComparison.Ordinal)) continue;

                    throw new PortBindingViolationException(
                        scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 行首列为空（第 {index + 1} 行）");
                }

                var key = separator.Length == 0
                    ? head
                    : string.Join(separator,
                        cells.Select(cell => PortValues.Text(cell))
                             .Where(cell => !string.IsNullOrEmpty(cell)));

                var value = spec.RowValue is { } rowValue
                    ? ScriptedExprEvaluator.Evaluate(rowValue, scope, input, deps, context)
                    : head;

                if (map.TryAdd(key, value)) continue;
                if (string.Equals(onDuplicate, "last", StringComparison.Ordinal))
                {
                    map[key] = value;
                    continue;
                }

                throw new PortBindingViolationException(
                    scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 的键 '{key}' 重复（第 {index + 1} 行）");
            }
            finally
            {
                scope.Pop();
            }
        }

        return map;
    }

    /// <summary>
    /// 存在性判定表（P3）：词元集 → 逐项布尔（<c>filter</c> 体内 <c>from:row</c> 即当前词元；
    /// 缺 <c>filter</c> 时按「词元非空」判定）。缺首列为空的词元直接跳过（不可作键）。
    /// </summary>
    private static Dictionary<string, object?> Presence(
        ScriptedArgSpec spec, EvalScope scope, IPortInput input, object?[] deps, string context)
    {
        var source = spec.Items ?? spec.Rows ?? throw new PortBindingViolationException(
            scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 的 presence 来源缺 items（词元集来源）");

        var tokens = PortValues.Elements(Resolve(source, scope, input, deps, $"{context} 的词元集")).ToArray();
        var map = new Dictionary<string, object?>(StringComparer.Ordinal);

        for (var index = 0; index < tokens.Length; index++)
        {
            if (PortValues.Text(tokens[index]) is not { Length: > 0 } token) continue;
            if (spec.Distinct && map.ContainsKey(token)) continue;

            scope.Push(EvalScope.RowFrame, tokens[index], index);
            try
            {
                map[token] = spec.Filter is { } filter
                    ? PortValues.Truthy(ScriptedExprEvaluator.Evaluate(filter, scope, input, deps, $"{context} 的判定"))
                    : tokens[index] is not null;
            }
            finally
            {
                scope.Pop();
            }
        }

        return map;
    }

    /// <summary>按 <c>convert</c> 与目标形参类型转换（无 <c>convert</c>：JSON 反序列化 / 原类型直投）。</summary>
    private static object? Convert(object? raw, ScriptedArgSpec spec, Type target, string portKey, string context)
    {
        if (raw is null)
        {
            if (spec.Default is { } fallback) return PortValues.Coerce(fallback, target);
            if (spec.Optional) return ReflectivePortResolver.DefaultFor(target);

            // E4：null 不得落到非可空值类型形参（否则延迟到 Invoke 才炸，诊断不可定位）。
            if (target.IsValueType && Nullable.GetUnderlyingType(target) is null)
                throw new PortBindingViolationException(
                    portKey, $"端口 '{portKey}' 的 {context} 缺值（null）不得赋给非可空形参 {target.Name}");

            return null;
        }

        switch (spec.Convert)
        {
            case "string":
                return PortValues.Text(raw);

            case "csv":
                return Csv(PortValues.Text(raw) ?? string.Empty, target, portKey, context);

            case "array":
                return ArrayLike(raw, spec, target, portKey, context);

            case "graph":
                return raw is JsonElement graph ? PortValues.ToGraph(graph) : raw;

            case "json":
                return PortValues.Coerce(
                    raw is JsonElement element
                        ? element
                        : JsonDocument.Parse(PortValues.Text(raw) ?? "null").RootElement,
                    target);

            case "bool":
                return PortValues.TryNormalize(raw, typeof(bool), out var flag) && flag is bool parsed
                    ? parsed
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

            case "object" or "passthrough":
                return PortValues.FitsDirect(raw, target)
                    ? raw
                    : throw Mismatch(portKey, context, raw, target.Name);

            case null or "":
                return Direct(raw, target, portKey, context);

            default:
                throw new PortBindingViolationException(
                    portKey,
                    $"端口 '{portKey}' 的 {context} 的 convert '{spec.Convert}' 未知" +
                    $"（{PortImplVocabulary.RenderList(PortImplVocabulary.ScriptedConversionEnum)}）");
        }
    }

    private static PortBindingViolationException Mismatch(string portKey, string context, object raw, string expected)
        => new(portKey, $"端口 '{portKey}' 的 {context} 的值 '{PortValues.Text(raw)}' 不能转换为 {expected}");

    /// <summary>无转换声明：走**值归一单源**（E4：JSON 标量 / CLR 数值 / 文本 → 目标类型的可比转换；不可归一 → 诊断）。</summary>
    private static object? Direct(object raw, Type target, string portKey, string context)
    {
        if (PortValues.TryNormalize(raw, target, out var normalized)) return normalized;

        throw Mismatch(portKey, context, raw, target.Name);
    }

    /// <summary>CSV 文本 → 目标集合类型（切分单源 = <see cref="PortValues.SplitCsv"/>：去空段 + 逐项去空白）。</summary>
    private static object? Csv(string text, Type target, string portKey, string context)
    {
        var parts = PortValues.SplitCsv(text).ToList();

        if (target.IsArray) return parts.ToArray();
        if (target == typeof(object) || target.IsAssignableFrom(parts.GetType())) return parts;

        throw Mismatch(portKey, context, text, target.Name);
    }

    /// <summary>
    /// 位置实参取值（越界 → fail-fast）：对象直投形态（<see cref="PortValues.IsDirectPositional"/>）原样保留原对象
    /// （不 <c>ToString</c> 文本化），其余按 <c>convert</c> 原义取文本。
    /// </summary>
    private static object? ArrayLike(
        object? raw, ScriptedArgSpec spec, Type target, string portKey, string context)
    {
        var element = ElementTypeOf(spec, target, portKey, context);
        var items = PortValues.Elements(raw).ToArray();
        var array = Array.CreateInstance(element, items.Length);

        for (var i = 0; i < items.Length; i++)
            array.SetValue(ConvertElement(items[i], element, portKey, context, i), i);

        if (target.IsInstanceOfType(array)) return array;

        if (target.IsGenericType && target.GetGenericTypeDefinition() == typeof(List<>))
        {
            var list = (System.Collections.IList)Activator.CreateInstance(target)!;
            foreach (var item in array) list.Add(item);
            return list;
        }

        return array;
    }

    private static Type ElementTypeOf(ScriptedArgSpec spec, Type target, string portKey, string context)
    {
        if (spec.ElementType is { Length: > 0 } name) return ComposedDeps.ResolveType(name, null);
        if (target.IsArray && target.GetElementType() is { } element) return element;

        if (target.IsGenericType
            && target.GetGenericArguments() is { Length: 1 } arguments
            && target != typeof(string)
            && typeof(System.Collections.IEnumerable).IsAssignableFrom(target))
        {
            return arguments[0];
        }

        throw new PortBindingViolationException(
            portKey,
            $"端口 '{portKey}' 的 {context} 的 array 转换需要集合 / 数组目标类型或 elementType 声明（当前 {target.Name}）");
    }

    /// <summary>
    /// 数组元素转换（E3）：先**原义解包**（JSON 标量 → CLR；数组 / 对象保持 <c>JsonElement</c>）再按元素类型分派 ——
    /// <c>Type</c> 按全名解析、枚举按名解析（忽略大小写，与 <c>SpecLiterals.ParseEnum</c> 同口径）、值类型走**归一单源**（E4）、
    /// 其余回落 STJ；非法元素 → 诊断含**元素下标**与期望类型。
    /// </summary>
    private static object? ConvertElement(object? value, Type element, string portKey, string context, int index)
    {
        if (value is null) return null;

        var literal = PortValues.UnwrapValue(value);
        if (element == typeof(object) || element.IsInstanceOfType(literal)) return literal;

        // 类型字面量元素（W22 / E3）：元素类型为 System.Type 时按类型全名解析（Type[] 的声明式装配）。
        if (element == typeof(Type) && literal is string typeName)
        {
            try
            {
                return ComposedDeps.ResolveType(typeName, null);
            }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException)
            {
                throw new PortBindingViolationException(
                    portKey,
                    $"端口 '{portKey}' 的 {context} 的第 {index} 个元素 '{typeName}' 不是可解析的类型（{ex.Message}）");
            }
        }

        var underlying = Nullable.GetUnderlyingType(element) ?? element;
        if (underlying.IsEnum && literal is string enumName)
        {
            try
            {
                return Enum.Parse(underlying, enumName, ignoreCase: true);
            }
            catch (Exception ex) when (ex is ArgumentException or OverflowException)
            {
                throw ElementMismatch(portKey, context, index, literal, element);
            }
        }

        if (PortValues.TryNormalize(literal, element, out var normalized)) return normalized;

        throw ElementMismatch(portKey, context, index, literal, element);
    }

    private static PortBindingViolationException ElementMismatch(
        string portKey, string context, int index, object value, Type element)
        => new(portKey,
            $"端口 '{portKey}' 的 {context} 的第 {index} 个元素 '{PortValues.Text(value)}'（{value.GetType().Name}）不能转为 {element.Name}");

    private static object? StaticMember(ScriptedArgSpec spec, string portKey, string context)
    {
        if (spec.Type is not { Length: > 0 } typeName)
            throw new PortBindingViolationException(
                portKey, $"端口 '{portKey}' 的 {context} 的 member 来源缺 type（声明类型全名）");

        if (string.IsNullOrWhiteSpace(spec.Key))
            throw new PortBindingViolationException(
                portKey, $"端口 '{portKey}' 的 {context} 的 member 来源缺 key（成员名）");

        var member = ReflectivePortResolver.ResolveMember(
            ComposedDeps.ResolveType(typeName, spec.GenericArgs), portKey, spec.Key!, isStatic: true);

        return member switch
        {
            PropertyInfo property => property.GetValue(null),
            FieldInfo field => field.GetValue(null),
            _ => null,
        };
    }

    /// <summary>
    /// 类型字面量取值（W22）：<c>type</c> = 类型全名（<c>genericArgs</c> 闭合开放泛型）→ <see cref="Type"/> 实例。
    /// <para>用于「把类型本身作实参」（结构探针的 <c>typeof(T)</c> 形态）；数组形态经 <c>convert:array</c> +
    /// <c>elementType:"System.Type"</c> 得到 <c>Type[]</c>。</para>
    /// </summary>
    private static Type TypeLiteral(ScriptedArgSpec spec, string portKey, string context)
        => spec.Type is { Length: > 0 } name
            ? ComposedDeps.ResolveType(name, spec.GenericArgs)
            : throw new PortBindingViolationException(
                portKey, $"端口 '{portKey}' 的 {context} 的 type 来源缺 type（类型全名）");

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

        var raw = input.Args[position];
        if (spec.NullLiteral && raw is string literal && string.Equals(literal, "null", StringComparison.Ordinal)) return null;

        if (PortValues.IsDirectPositional(spec.Convert, raw)) return raw;

        return raw as string ?? raw?.ToString() ?? string.Empty;
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
