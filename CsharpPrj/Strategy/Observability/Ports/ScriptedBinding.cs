using System.Reflection;
using Strategy.Observability.Diagnostics;

namespace Strategy.Observability.Ports;

/// <summary>
/// 声明式**脚本绑定**求值器（消除通道 C5）：按声明序执行「输入绑定 → 依赖构建 → 脚本步骤 → 投影 → 载体」，
/// 产出 <c>字段 → 值</c> 字典（与旧夹具编排逐字等价）。
/// <para>定位：与 <see cref="ComposedBinding"/> 并列的机制件——差别为「可遍历用例声明的数据 + 可用上一步结果再调 +
/// 一般取值表达式 + 载体形态」；同为零领域词汇（类型 / 方法 / 字段名一律不透明字符串）。</para>
/// <para>依赖构建与调用折叠复用单源（<see cref="ComposedDeps"/> / <see cref="PortInvocation"/>）。</para>
/// </summary>
public sealed class ScriptedBinding : IPortBinding
{
    private readonly ScriptedBindingSpec _spec;
    private readonly IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>> _factories;
    private static readonly object?[] NoDeps = Array.Empty<object?>();

    /// <summary>声明的收集槽名集（步骤 / 遍历 / 投影 collect / sameAsLast）。</summary>
    private readonly IReadOnlySet<string> _collections;

    /// <summary>按声明构造绑定：无脚本且无投影、载体冲突、引用形态残缺 → 即抛（fail-fast，防静默绑错）。</summary>
    public ScriptedBinding(ScriptedBindingSpec spec)
    {
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
        _factories = spec.Factories;
        _collections = Collections(spec);

        if (spec.Script.Count == 0 && spec.Project.Count == 0)
            throw new PortBindingViolationException(
                spec.PortKey, $"端口 '{spec.PortKey}' 声明 scripted 绑定但 script 与 project 均为空");

        if (spec.Deliver.Defer && spec.Deliver.Issues)
            throw new PortBindingViolationException(
                spec.PortKey, $"端口 '{spec.PortKey}' 的载体 defer 与 issues 互斥（二选一）");
    }

    public ValueTask<object?> InvokeAsync(IPortInput input, CancellationToken ct = default)
        => ValueTask.FromResult(Invoke(input));

    /// <summary>求值（本件唯一执行序）：bind → deps → script → project → deliver。</summary>
    private object? Invoke(IPortInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        return _spec.Deliver.Defer
            ? new DeferredAction(() => Execute(input))
            : Execute(input);
    }

    /// <summary>即时执行（<c>deliver.defer</c> 时由载体在断言期再次调用）。</summary>
    private object? Execute(IPortInput input)
    {
        var scope = new ScriptedScope(_spec.PortKey);

        foreach (var name in _collections) scope.Collect(name);

        foreach (var (name, bind) in _spec.Binds)
            scope.Set(name, ScriptedValue.ResolveTyped(
                bind, typeof(object), scope, input, NoDeps, $"输入绑定 '{name}'"));

        var deps = ComposedDeps.Build(_spec.Deps, input, _factories, _spec.PortKey);

        Run(_spec.Script, scope, input, deps);

        var product = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in _spec.Project) Project(field, product, scope, input, deps);

        return _spec.Deliver.Issues ? new PortIssues(IssueList(IssueSource(product, scope))) : product;
    }

    /// <summary>按声明序执行步骤（<c>When</c> 为假即短路跳过；形态未知 → fail-fast）。返回末步产物值。</summary>
    private object? Run(IReadOnlyList<ScriptedStep> steps, ScriptedScope scope, IPortInput input, object?[] deps)
    {
        object? last = null;

        foreach (var step in steps)
        {
            if (step.When is { } condition
                && !Truthy(ScriptedValue.Resolve(condition, scope, input, deps, "步骤条件 when")))
            {
                continue;
            }

            if (step.New is { } construct) last = RunNew(step, construct, scope, input, deps);
            else if (step.Invoke is { } invoke) last = RunInvoke(step, invoke, scope, input, deps);
            else if (step.Foreach is { } loop) last = RunForeach(step, loop, scope, input, deps);
            else
                throw new PortBindingViolationException(
                    _spec.PortKey, $"端口 '{_spec.PortKey}' 的脚本步骤未声明形态（new|invoke|foreach）");
        }

        return last;
    }

    /// <summary>构造步骤：反射构造 → 写槽 → 收集。返回构造对象。</summary>
    private object? RunNew(
        ScriptedStep step, ScriptedNewSpec construct, ScriptedScope scope, IPortInput input, object?[] deps)
    {
        var context = $"步骤 new '{construct.As}'";
        var type = ComposedDeps.ResolveType(construct.Type, construct.GenericArgs);
        var declared = construct.Args ?? Array.Empty<ScriptedArgSpec>();
        var parameters = ComposedDeps
            .PickConstructor(_spec.PortKey, type, declared.Count, context)
            .GetParameters();

        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
            args[i] = i < declared.Count
                ? ScriptedValue.ResolveTyped(
                    declared[i], parameters[i].ParameterType, scope, input, deps, $"{context} 第 {i} 个实参")
                : ReflectivePortResolver.DefaultFor(parameters[i].ParameterType);

        var created = Activator.CreateInstance(type, args);
        if (!string.IsNullOrWhiteSpace(construct.As)) scope.Set(construct.As, created);
        Collect(step, created, scope);
        return created;
    }

    /// <summary>调用步骤：解析目标（静态 / 依赖实例）→ 构参 → 调用（期望异常归一 + 异步折取）→ 写槽 / 出参捕获 / 收集 → 可选跳出遍历。</summary>
    private object? RunInvoke(
        ScriptedStep step, ScriptedInvokeSpec invoke, ScriptedScope scope, IPortInput input, object?[] deps)
    {
        if (string.IsNullOrWhiteSpace(invoke.Method))
            throw new PortBindingViolationException(
                _spec.PortKey, $"端口 '{_spec.PortKey}' 的调用步骤缺少 method");

        var context = $"步骤 invoke '{invoke.Method}'";
        var isStatic = invoke.Dep is null;
        var type = isStatic ? StaticType(invoke, context) : InstanceType(invoke, context);
        var expect = invoke.Expect is { Length: > 0 } expectName
            ? ComposedDeps.ResolveType(expectName, null)
            : null;
        var declared = invoke.Args ?? Array.Empty<ScriptedArgSpec>();
        var instance = isStatic
            ? null
            : ComposedDeps.Dep(deps, invoke.Dep!.Value, invoke.Dep.Value, _spec.PortKey);

        object? result;
        if (invoke.IsProperty)
        {
            var member = ReflectivePortResolver.ResolveMember(type, _spec.PortKey, invoke.Method, isStatic);
            result = PortInvocation.Execute(
                () => member switch
                {
                    PropertyInfo property => property.GetValue(instance),
                    FieldInfo field => field.GetValue(instance),
                    _ => null,
                },
                expect);
        }
        else
        {
            var method = ReflectivePortResolver.ResolveMethod(
                type, _spec.PortKey, invoke.Method, declared.Count, isStatic);
            var parameters = method.GetParameters();
            var args = new object?[parameters.Length];
            for (var i = 0; i < parameters.Length; i++)
                args[i] = i < declared.Count
                    ? ScriptedValue.ResolveTyped(
                        declared[i], parameters[i].ParameterType, scope, input, deps, $"{context} 第 {i} 个实参")
                    : ReflectivePortResolver.DefaultFor(parameters[i].ParameterType);

            result = PortInvocation.Execute(() => method.Invoke(instance, args), expect);
            CaptureOut(invoke, parameters, args, scope, context);
        }

        result = PortInvocation.Fold(result, invoke.Await);

        if (invoke.As is { Length: > 0 } asName) scope.Set(asName, result);

        // break 语义：声明 expect 时「命中即跳出」（首遇失败即停）；未声明 expect 时执行后即跳出。
        if (invoke.Break && (expect is null || (result is not null && expect.IsInstanceOfType(result))))
            throw new BreakSignal();

        // 收集置于跳出判定之后：被 break 中止的迭代「未完成」→ 其收集不入列
        // （夹具「仅登记成功者入列」等口径由此对齐；foreach 级收集仍记录本次迭代末产物，见 RunForeach）。
        Collect(step, result, scope);

        return result;
    }

    /// <summary>出参捕获（G2）：把方法的 <c>out</c> 形参按位写入槽（首个用声明名，多个按序加后缀）。</summary>
    private void CaptureOut(
        ScriptedInvokeSpec invoke, ParameterInfo[] parameters, object?[] args, ScriptedScope scope, string context)
    {
        if (invoke.Out is not { Length: > 0 } name) return;

        var written = 0;
        for (var i = 0; i < parameters.Length; i++)
        {
            if (!parameters[i].IsOut && !parameters[i].ParameterType.IsByRef) continue;

            scope.Set(written == 0 ? name : $"{name}{written}", args[i]);
            written++;
        }

        if (written == 0)
            throw new PortBindingViolationException(
                _spec.PortKey,
                $"端口 '{_spec.PortKey}' 的 {context} 声明 out='{name}' 但目标方法没有 out / ref 形参");
    }

    /// <summary>静态调用目标类型（<c>target</c> 必填）。</summary>
    private Type StaticType(ScriptedInvokeSpec invoke, string context)
        => invoke.Target is { Length: > 0 } target
            ? ComposedDeps.ResolveType(target, null)
            : throw new PortBindingViolationException(
                _spec.PortKey,
                $"端口 '{_spec.PortKey}' 的 {context} 缺 target（静态调用）或 dep（实例调用）");

    /// <summary>实例调用目标类型（取依赖声明的 contract / new 类型）。</summary>
    private Type InstanceType(ScriptedInvokeSpec invoke, string context)
    {
        var index = invoke.Dep ?? -1;
        if (index < 0 || index >= _spec.Deps.Count)
            throw new PortBindingViolationException(
                _spec.PortKey,
                $"端口 '{_spec.PortKey}' 的 {context} 引用了不存在的依赖下标 {index}（依赖 {_spec.Deps.Count} 个）");

        var dep = _spec.Deps[index];
        var typeName = dep.Contract ?? dep.New;
        if (typeName is not { Length: > 0 })
            throw new PortBindingViolationException(
                _spec.PortKey,
                $"端口 '{_spec.PortKey}' 的 {context} 对 dep[{index}] 取实例成员，但该依赖未声明 contract / new" +
                "（实例类型不可知）");

        return ComposedDeps.ResolveType(typeName, dep.GenericArgs);
    }

    /// <summary>遍历步骤：取集合（槽 / 表达式）逐项入栈循环变量 → 过滤 → 执行体内步骤（<c>break</c> 跳出本层）。返回末次迭代产物值。</summary>
    private object? RunForeach(
        ScriptedStep step, ScriptedForeachSpec loop, ScriptedScope scope, IPortInput input, object?[] deps)
    {
        if (string.IsNullOrWhiteSpace(loop.As))
            throw new PortBindingViolationException(
                _spec.PortKey, $"端口 '{_spec.PortKey}' 的遍历步骤缺少 as（循环变量名）");

        var context = $"遍历 '{loop.As}'";
        var items = loop.Slot is { Length: > 0 } slotName
            ? scope.Slot(slotName, context)
            : ScriptedExprEvaluator.Evaluate(
                loop.Items ?? throw new PortBindingViolationException(
                    _spec.PortKey, $"端口 '{_spec.PortKey}' 的 {context} 缺集合来源（slot 或 items）"),
                scope, input, deps, context);

        var body = loop.Do ?? Array.Empty<ScriptedStep>();
        var index = 0;
        object? last = null;

        foreach (var item in PortValues.Elements(items))
        {
            var produced = last;
            scope.Push(loop.As, item, index);
            try
            {
                if (loop.Where is not { } filter
                    || Truthy(ScriptedValue.Resolve(filter, scope, input, deps, $"{context} 的过滤条件")))
                {
                    produced = Run(body, scope, input, deps);
                }
            }
            catch (BreakSignal)
            {
                scope.Pop();
                Collect(step, produced, scope);
                return produced;
            }

            scope.Pop();
            Collect(step, produced, scope);
            last = produced;
            index++;
        }

        return last;
    }

    /// <summary>投影：字段 ← 表达式；<c>spread</c> 形态改为动态键逐项写入产物。</summary>
    private void Project(
        ScriptedFieldSpec field, Dictionary<string, object?> product,
        ScriptedScope scope, IPortInput input, object?[] deps)
    {
        var value = ScriptedExprEvaluator.Evaluate(field.Expr, scope, input, deps, field.Field);

        if (field.Expr.Spread is not null && value is IReadOnlyDictionary<string, object?> entries)
        {
            foreach (var (key, item) in entries) product[key] = item;
            return;
        }

        product[field.Field] = value;
    }

    /// <summary>清单来源：声明字段（槽优先，回落产物字段）→ 首个产物字段。</summary>
    private object? IssueSource(Dictionary<string, object?> product, ScriptedScope scope)
    {
        if (_spec.Deliver.IssueField is { Length: > 0 } name)
        {
            if (scope.Has(name)) return scope.Slot(name, "载体 issues");
            if (product.TryGetValue(name, out var declared)) return declared;

            throw new PortBindingViolationException(
                _spec.PortKey,
                $"端口 '{_spec.PortKey}' 的载体 issues 引用了未定义的字段 / 槽 '{name}'");
        }

        return product.Count > 0 ? product.Values.ToArray() : null;
    }

    /// <summary>问题清单归一：异常 → 其消息；集合 → 逐项文本；null → 空清单；其余 → 单项文本。</summary>
    private static IReadOnlyList<string> IssueList(object? value)
        => value switch
        {
            null => Array.Empty<string>(),
            string text => new[] { text },
            Exception exception => new[] { exception.Message },
            _ => PortValues.Elements(value).Select(item => PortValues.Text(item) ?? string.Empty).ToArray(),
        };

    /// <summary>收集声明（可多源：步产物 ∪ 指定槽当前值）。</summary>
    private static void Collect(ScriptedStep step, object? product, ScriptedScope scope)
    {
        if (step.Collect is not { Count: > 0 } collects) return;

        foreach (var collect in collects)
        {
            var value = collect.From is { Length: > 0 } source
                ? scope.Slot(source, $"步骤 collect 的 from 槽 '{source}'")
                : product;
            scope.Collect(collect.Slot).Add(value);
        }
    }

    /// <summary>条件真值（null / false / 空串 / 空 JSON → 假；其余 → 真）。</summary>
    private static bool Truthy(object? value) => value switch
    {
        null => false,
        bool flag => flag,
        string text => text.Length > 0 && !string.Equals(text, "false", StringComparison.OrdinalIgnoreCase),
        System.Text.Json.JsonElement element => element.ValueKind switch
        {
            System.Text.Json.JsonValueKind.True => true,
            System.Text.Json.JsonValueKind.False or System.Text.Json.JsonValueKind.Null => false,
            System.Text.Json.JsonValueKind.String => (element.GetString() ?? string.Empty).Length > 0,
            System.Text.Json.JsonValueKind.Number => element.GetDouble() != 0,
            System.Text.Json.JsonValueKind.Array => element.GetArrayLength() > 0,
            System.Text.Json.JsonValueKind.Object => element.EnumerateObject().Any(),
            _ => false,
        },
        _ => true,
    };

    /// <summary>遍历跳出信号（<c>break: true</c> → 跳出最近一层 <c>foreach</c>）。</summary>
    private sealed class BreakSignal : Exception;

    /// <summary>声明的收集槽名集（执行前预建空集合：短路 / 零迭代时投影仍可取空清单）。</summary>
    private static IReadOnlySet<string> Collections(ScriptedBindingSpec spec)
    {
        var names = new HashSet<string>(StringComparer.Ordinal);
        CollectSteps(spec.Script, names);

        foreach (var field in spec.Project) CollectExpr(field.Expr, names);

        return names;
    }

    private static void CollectSteps(IReadOnlyList<ScriptedStep> steps, HashSet<string> names)
    {
        foreach (var step in steps)
        {
            foreach (var collect in step.Collect ?? Array.Empty<ScriptedCollectSpec>()) names.Add(collect.Slot);
            if (step.Foreach is { } loop) CollectSteps(loop.Do ?? Array.Empty<ScriptedStep>(), names);
        }
    }

    private static void CollectExpr(ScriptedExpr expr, HashSet<string> names)
    {
        if (expr.Collect is { Length: > 0 } collect) names.Add(collect);
        if (expr.SameAsLast is { Length: > 0 } other) names.Add(other);

        foreach (var sub in Subs(expr)) CollectExpr(sub, names);
    }

    private static IEnumerable<ScriptedExpr> Subs(ScriptedExpr expr)
    {
        foreach (var candidate in new[]
        {
            expr.Count, expr.NotNull, expr.AsText, expr.TypeName, expr.Message, expr.Thrown,
            expr.Contains, expr.Of, expr.All, expr.Any, expr.AllIn, expr.Spread,
        })
        {
            if (candidate is not null) yield return candidate;
        }

        foreach (var item in expr.Concat ?? Array.Empty<ScriptedExpr>()) yield return item;
        foreach (var item in expr.CountEqual ?? Array.Empty<ScriptedExpr>()) yield return item;
    }
}
