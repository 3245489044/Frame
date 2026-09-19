using System.Reflection;
using Strategy.Observability.Diagnostics;

namespace Strategy.Observability.Ports;

using DataCenter.Protocol.Standards;

/// <summary>
/// 调用执行单源（步骤 <c>invoke</c> 与表达式内调用共用）：目标解析 → 实参构造 → 期望异常归一 →
/// 异步折取 → 出参捕获。零领域词汇（类型 / 方法 / 形参名一律不透明字符串）。
/// </summary>
internal static class ScriptedInvokeRunner
{
    /// <summary>调用产物（结果 + 形参表 + 实参数组，供出参捕获与后续展开）。</summary>
    internal sealed record Outcome(object? Result, ParameterInfo[] Parameters, object?[] Args);

    /// <summary>
    /// 执行一次调用声明。<paramref name="specs"/> = 依赖声明序列（供实例调用的静态类型解析）；
    /// 表达式内调用可传 null（按依赖对象的运行时类型解析）。
    /// </summary>
    internal static Outcome Run(
        string portKey, InvokeStandard invoke, IReadOnlyList<DepStandard>? specs,
        EvalScope scope, IPortInput input, object?[] resolvedDeps)
    {
        if (string.IsNullOrWhiteSpace(invoke.Method))
            throw new PortBindingViolationException(portKey, $"端口 '{portKey}' 的调用声明缺少 method");

        // W24：对**表达式值**调用（委托 / 实例）——目标类型取运行时类型，声明只给方法名与实参。
        if (invoke.Value is { } declaredTarget)
        {
            var target = ScriptedValue.Resolve(
                declaredTarget, scope, input, resolvedDeps, $"调用 '{invoke.Method}' 的目标取值");

            if (target is null)
                throw new PortBindingViolationException(
                    portKey, $"端口 '{portKey}' 的调用 '{invoke.Method}' 的目标取值为 null（无可调用实例）");

            return RunOnValue(portKey, invoke, target, scope, input, resolvedDeps);
        }

        var context = $"调用 '{invoke.Method}'";
        var isStatic = invoke.Dep is null;
        var type = isStatic
            ? StaticType(portKey, invoke, context)
            : InstanceType(portKey, invoke, specs, resolvedDeps, context);
        var expect = ExpectTypes(invoke.Expect);
        var declared = invoke.Args ?? Array.Empty<ValueSourceStandard>();
        var instance = isStatic
            ? null
            : ComposedDeps.Dep(resolvedDeps, invoke.Dep!.Value, invoke.Dep.Value, portKey);

        if (invoke.IsProperty)
        {
            var member = ReflectivePortResolver.ResolveMember(
                type, portKey, invoke.Method, isStatic, invoke.NonPublic);
            var read = PortInvocation.Invoke(
                () => ReflectivePortResolver.ReadMember(member, instance), expect);

            return new Outcome(read, Array.Empty<ParameterInfo>(), Array.Empty<object?>());
        }

        var method = Close(portKey, invoke, PickMethod(
            portKey, context, type, invoke.Method, declared, isStatic, invoke.NonPublic, scope, input, resolvedDeps,
            invoke.GenericArgs));
        var parameters = method.GetParameters();
        var args = Bind(portKey, $"{context}（{type.Name}.{method.Name}）", declared, parameters, scope, input, resolvedDeps);

        var result = PortInvocation.Invoke(() => method.Invoke(instance, args), expect);
        return new Outcome(result, parameters, args);
    }

    /// <summary>
    /// 对**取值**调用（W24）：委托 → 其 <c>Invoke</c>（等价于夹具里 <c>next(ct)</c> 一类的「调用传入的委托」）；
    /// 其余 → 按其运行时类型解析实例方法 / 成员（可含非公开，W20）。
    /// </summary>
    private static Outcome RunOnValue(
        string portKey, InvokeStandard invoke, object target, EvalScope scope,
        IPortInput input, object?[] deps)
    {
        var context = $"调用 '{invoke.Method}'";

        // E9：对**取值调用**前先原义解包（JSON 标量 → CLR；数组 / 对象保持 JsonElement），
        // 使「JSON 字符串元素上的实例方法」等声明可直达真实载体；方法解析亦按实参择优（同 E1）。
        var unwrapped = PortValues.UnwrapValue(target);
        if (unwrapped is null)
            throw new PortBindingViolationException(
                portKey, $"端口 '{portKey}' 的 {context} 的目标取值解包后为 null（无可调用实例）");

        var type = unwrapped.GetType();
        var expect = ExpectTypes(invoke.Expect);

        if (unwrapped is Delegate)
        {
            var call = type.GetMethod("Invoke")
                ?? throw new PortBindingViolationException(
                    portKey, $"端口 '{portKey}' 的 {context} 的目标委托 '{type.Name}' 缺 Invoke 方法");

            var declared = invoke.Args ?? Array.Empty<ValueSourceStandard>();
            var parameters = call.GetParameters();
            var bound = Bind(portKey, $"{context}（委托 {type.Name}）", declared, parameters, scope, input, deps);
            var output = PortInvocation.Invoke(() => call.Invoke(unwrapped, bound), expect);
            return new Outcome(output, parameters, bound);
        }

        if (invoke.IsProperty)
        {
            var member = ReflectivePortResolver.ResolveMember(
                type, portKey, invoke.Method, isStatic: false, invoke.NonPublic);
            var read = PortInvocation.Invoke(
                () => ReflectivePortResolver.ReadMember(member, unwrapped), expect);

            return new Outcome(read, Array.Empty<ParameterInfo>(), Array.Empty<object?>());
        }

        var declaredArgs = invoke.Args ?? Array.Empty<ValueSourceStandard>();
        var method = Close(portKey, invoke, PickMethod(
            portKey, context, type, invoke.Method, declaredArgs, isStatic: false, invoke.NonPublic,
            scope, input, deps, invoke.GenericArgs));
        var methodParameters = method.GetParameters();
        var args = Bind(
            portKey, $"{context}（{type.Name}.{method.Name}）", declaredArgs, methodParameters, scope, input, deps);

        var result = PortInvocation.Invoke(() => method.Invoke(unwrapped, args), expect);
        return new Outcome(result, methodParameters, args);
    }

    /// <summary>
    /// 方法解析择优（E1 / E9 共用「可绑定度」单源）：候选按实参试绑定择优；无法择优（并列）时保持旧口径；
    /// 全部不可绑定 → fail-fast 并列候选签名。
    /// </summary>
    private static MethodInfo PickMethod(
        string portKey, string context, Type type, string methodName, IReadOnlyList<ValueSourceStandard> declared,
        bool isStatic, bool nonPublic, EvalScope scope, IPortInput input, object?[] deps,
        IReadOnlyList<string>? genericArgs)
        => ReflectivePortResolver.ResolveMethod(
            type, portKey, methodName, declared.Count, isStatic, nonPublic,
            candidate => TryBindCandidate(
                portKey, context, type, methodName, declared, candidate, genericArgs, scope, input, deps),
            context,
            genericArgs is { Count: > 0 } args ? args.Count : null);

    /// <summary>
    /// 候选试绑定（W4 修正）：声明 <c>genericArgs</c> 时**先闭合、再按闭合后形参试绑定**（未闭合的泛型形参
    /// 无法参与类型匹配 ⇒ 泛型重载被误判不可绑定，旧口径因此误选非泛型重载并报「不是开放泛型方法」）；
    /// 未声明时保持旧口径（不做泛型过滤，避免无谓回归）。
    /// </summary>
    private static object?[]? TryBindCandidate(
        string portKey, string context, Type type, string methodName, IReadOnlyList<ValueSourceStandard> declared,
        MethodInfo candidate, IReadOnlyList<string>? genericArgs,
        EvalScope scope, IPortInput input, object?[] deps)
    {
        var label = $"{context}（{type.Name}.{methodName}）";

        if (genericArgs is not { Count: > 0 } names)
            return TryBind(portKey, label, declared, candidate.GetParameters(), scope, input, deps);

        if (!candidate.IsGenericMethodDefinition) return null;   // 声明了闭合实参 ⇒ 非泛型候选不可选

        MethodInfo closed;
        try
        {
            closed = CloseMethod(portKey, methodName, candidate, names);
        }
        catch (PortBindingViolationException)
        {
            return null;   // 该候选不可闭合（数量不符 / 闭合失败）⇒ 交其它候选
        }

        return TryBind(portKey, label, declared, closed.GetParameters(), scope, input, deps);
    }

    /// <summary>
    /// 实参绑定单源（W13）：**位置绑定**（无 <c>named</c> 的项按序填入首个未占用形参位）+ **命名绑定**
    /// （<c>named</c> = 形参名，Ordinal 精确匹配）；未声明位取该形参类型默认值；重复绑定 / 名字不存在 / 超出形参数 → 即抛。
    /// <para>构造（<see cref="ScriptedConstruction"/>）与调用（<see cref="Run"/>）共用本实现，避免两处各写一套绑定口径。</para>
    /// </summary>
    internal static object?[] Bind(
        string portKey, string context, IReadOnlyList<ValueSourceStandard> declared, ParameterInfo[] parameters,
        EvalScope scope, IPortInput input, object?[] deps)
    {
        var args = new object?[parameters.Length];
        var assigned = new bool[parameters.Length];
        var cursor = 0;

        foreach (var spec in declared)
        {
            int index;

            if (spec.Named is { Length: > 0 } named)
            {
                index = Array.FindIndex(
                    parameters, parameter => string.Equals(parameter.Name, named, StringComparison.Ordinal));

                if (index < 0)
                    throw new PortBindingViolationException(
                        portKey, $"端口 '{portKey}' 的 {context} 命名实参 '{named}' 不在目标形参表中（可选：{ParameterNames(parameters)}）");
            }
            else
            {
                while (cursor < parameters.Length && assigned[cursor]) cursor++;

                if (cursor >= parameters.Length)
                    throw new PortBindingViolationException(
                        portKey, $"端口 '{portKey}' 的 {context} 位置实参个数超出形参数（{parameters.Length}）");

                index = cursor;
            }

            if (assigned[index])
                throw new PortBindingViolationException(
                    portKey, $"端口 '{portKey}' 的 {context} 形参 '{parameters[index].Name}' 被重复绑定");

            args[index] = ScriptedValue.ResolveTyped(
                spec, parameters[index].ParameterType, scope, input, deps,
                $"{context} 形参 '{parameters[index].Name}'");
            assigned[index] = true;
        }

        for (var i = 0; i < parameters.Length; i++)
            if (!assigned[i]) args[i] = ReflectivePortResolver.DefaultFor(parameters[i].ParameterType);

        return args;
    }

    /// <summary>试绑定（重载择优用）：绑定失败（<see cref="PortBindingViolationException"/>）→ null（不可绑定），其余异常照抛。</summary>
    internal static object?[]? TryBind(
        string portKey, string context, IReadOnlyList<ValueSourceStandard> declared,
        ParameterInfo[] parameters, EvalScope scope, IPortInput input, object?[] deps)
    {
        try
        {
            return Bind(portKey, context, declared, parameters, scope, input, deps);
        }
        catch (PortBindingViolationException)
        {
            return null;
        }
    }

    /// <summary>形参名清单（诊断文案：命名实参拼错时列出可选名）。</summary>
    private static string ParameterNames(ParameterInfo[] parameters)
        => parameters.Length == 0 ? "(无)" : string.Join(", ", parameters.Select(parameter => parameter.Name));

    /// <summary>
    /// 对**显式实例**执行调用声明（W6 对象图链式调用）：把实例追加为末位依赖（<c>specs=null</c> ⇒ 按运行时类型解析），
    /// 与 <see cref="Run"/> 共用同一执行路径（零重复实现）；链式实参里的 <c>dep</c> 下标仍指向原依赖序。
    /// </summary>
    internal static Outcome RunOn(
        string portKey, InvokeStandard invoke, object instance, EvalScope scope,
        IPortInput input, object?[] resolvedDeps)
    {
        var own = new object?[resolvedDeps.Length + 1];
        resolvedDeps.CopyTo(own, 0);
        own[^1] = instance;

        return Run(portKey, invoke with { Dep = resolvedDeps.Length }, specs: null, scope, input, own);
    }

    /// <summary>泛型方法闭合（W4）：声明 <c>genericArgs</c> 时按序闭合；非开放泛型方法声明了即抛（防静默错配）。</summary>
    private static MethodInfo Close(string portKey, InvokeStandard invoke, MethodInfo method)
        => invoke.GenericArgs is { Count: > 0 } names
            ? CloseMethod(portKey, invoke.Method, method, names)
            : method;

    /// <summary>
    /// 泛型闭合单源（W4）。消费方：① <see cref="Close"/>（解析结果闭合）；② <see cref="PickMethod"/> 的择优试绑定
    /// ——**声明 <c>genericArgs</c> 时须先闭合再按闭合后形参试绑定**：未替换的泛型形参无法参与类型匹配，
    /// 泛型重载会被误判为不可绑定，进而误选非泛型重载并报「不是开放泛型方法」。
    /// </summary>
    internal static MethodInfo CloseMethod(
        string portKey, string methodName, MethodInfo method, IReadOnlyList<string> names)
    {
        if (!method.IsGenericMethodDefinition)
            throw new PortBindingViolationException(
                portKey,
                $"端口 '{portKey}' 的调用 '{methodName}' 声明了 genericArgs，但目标方法不是开放泛型方法");

        var types = names.Select(name => ComposedDeps.ResolveType(name, null)).ToArray();
        if (types.Length != method.GetGenericArguments().Length)
            throw new PortBindingViolationException(
                portKey,
                $"端口 '{portKey}' 的调用 '{methodName}' 的 genericArgs 数量 {types.Length} 与其泛型形参数量 {method.GetGenericArguments().Length} 不一致");

        try
        {
            return method.MakeGenericMethod(types);
        }
        catch (ArgumentException ex)
        {
            throw new PortBindingViolationException(
                portKey, $"端口 '{portKey}' 的调用 '{methodName}' 的泛型闭合失败（{ex.Message}）");
        }
    }

    /// <summary>出参捕获（G2）：把 <c>out</c> / <c>ref</c> 形参按位写入槽（首个用声明名，多个按序加后缀）。</summary>
    internal static void CaptureOut(
        string portKey, InvokeStandard invoke, ParameterInfo[] parameters, object?[] args,
        EvalScope scope, string context)
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
                portKey, $"端口 '{portKey}' 的 {context} 声明 out='{name}' 但目标方法没有 out / ref 形参");
    }

    /// <summary>期望异常类型声明解析（<c>|</c> 分隔多类型；缺省 → 不捕获任何异常）。</summary>
    internal static IReadOnlyList<Type>? ExpectTypes(string? declared)
        => declared is { Length: > 0 } text
            ? text.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                .Select(name => ComposedDeps.ResolveType(name, null))
                .ToArray()
            : null;

    /// <summary>跳出判定：声明 <c>expect</c> 时「命中期望异常即跳出」，未声明时执行后即跳出。</summary>
    internal static bool ShouldBreak(InvokeStandard invoke, object? result)
        => ExpectTypes(invoke.Expect) is not { } expect
           || (result is not null && expect.Any(type => type.IsInstanceOfType(result)));

    /// <summary>静态调用目标类型（<c>target</c> 必填）。</summary>
    private static Type StaticType(string portKey, InvokeStandard invoke, string context)
        => invoke.Target is { Length: > 0 } target
            ? ComposedDeps.ResolveType(target, null)
            : throw new PortBindingViolationException(
                portKey, $"端口 '{portKey}' 的 {context} 缺 target（静态调用）或 dep（实例调用）");

    /// <summary>实例调用目标类型（依赖声明的 contract / new；未给声明时取运行时类型）。</summary>
    private static Type InstanceType(
        string portKey, InvokeStandard invoke, IReadOnlyList<DepStandard>? specs,
        object?[] resolvedDeps, string context)
    {
        var index = invoke.Dep ?? -1;
        if (specs is null)
            return ComposedDeps.Dep(resolvedDeps, index, index, portKey)?.GetType()
                ?? throw new PortBindingViolationException(
                    portKey, $"端口 '{portKey}' 的 {context} 的依赖下标 {index} 无实例（类型不可知）");

        if (index < 0 || index >= specs.Count)
            throw new PortBindingViolationException(
                portKey, $"端口 '{portKey}' 的 {context} 引用了不存在的依赖下标 {index}（依赖 {specs.Count} 个）");

        return ComposedDeps.DeclaredInstanceType(specs, index, portKey, context);
    }
}
