namespace Strategy.Observability.Ports;

using System.Reflection;
using DataCenter.Protocol.Standards;
using Strategy.Observability.Diagnostics;

/// <summary>
/// 调用算子（R3-1）：解析目标成员 → 逐位实参绑定（唯一转换表）→ 调用归一。
/// <para>
/// **唯一实现点**：求值引擎的 <c>Call</c> 指令与该算子的构造期解析前置共用同一份代码，
/// 消除「绑定类各持一份解析/转换」的漂移面（D-2）；诊断文案与迁移前逐字一致（RF-5）。
/// </para>
/// <para>入参为**显式三元组**（端口键 / target / method + 实参声明）：不再中转绑定声明记录（A2 收敛后声明载体唯一）。</para>
/// </summary>
internal static class PortCall
{
    /// <summary>声明 → 目标方法（构造期 fail-fast：缺 <c>target/method</c> 或不可解析即抛）。</summary>
    internal static MethodInfo Resolve(
        string portKey, string? target, string? method, IReadOnlyList<PortArgStandard>? args = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portKey);

        if (string.IsNullOrWhiteSpace(target) || string.IsNullOrWhiteSpace(method))
            throw new PortBindingViolationException(portKey, PortTerminalReasons.ReflectiveMissingTarget(portKey));

        return ReflectivePortResolver.ResolveMethod(portKey, method, args?.Count ?? 0, ReflectivePortResolver.ResolveType(target));
    }

    /// <summary>按声明转换位置实参；未声明槽位（如尾部注入参数）取该类型默认值。</summary>
    internal static object?[] BuildArguments(
        string portKey, IReadOnlyList<PortArgStandard>? args, MethodInfo method, IPortInput input)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(portKey);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(input);

        var declared = args ?? Array.Empty<PortArgStandard>();
        var parameters = method.GetParameters();
        var values = new object?[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
            values[i] = i < declared.Count
                ? Convert(portKey, declared, parameters[i], declared[i], input, i)
                : ReflectivePortResolver.DefaultFor(parameters[i]);

        return values;
    }

    /// <summary>调用目标方法；业务异常原样上抛（反射 TargetInvocationException 拆包走调用单源，错误分类交调用记录）。</summary>
    internal static object? Invoke(MethodInfo method, object?[] arguments)
        => PortInvocation.ExecuteAny(() => method.Invoke(null, arguments), expect: null);

    private static object? Convert(
        string portKey, IReadOnlyList<PortArgStandard> args,
        ParameterInfo parameter, PortArgStandard arg, IPortInput input, int index)
    {
        if (string.Equals(arg.Convert, "json", StringComparison.Ordinal))
            return FromWith(portKey, parameter, arg, input);

        var present = index < input.Args.Count && input.Args[index] is not null;
        if (!present)
        {
            if (!arg.Optional)
                throw new PortBindingViolationException(
                    portKey,
                    $"{portKey} 需要第 {index} 个位置实参（声明 {DeclaredText(args)}），实际只有 {input.Args.Count} 个");

            return arg.Default is { } fallback
                ? ReflectivePortResolver.FromJson(fallback, parameter.ParameterType)
                : ReflectivePortResolver.DefaultFor(parameter);
        }

        var raw = input.Args[index];
        if (arg.NullLiteral && raw is string literal && string.Equals(literal, "null", StringComparison.Ordinal)) return null;

        if (PortValues.IsDirectPositional(arg.Convert, raw))
            return PortValues.FitsDirect(raw, parameter.ParameterType)
                ? raw
                : throw new PortBindingViolationException(
                    portKey,
                    $"{portKey} 第 {index} 个位置实参按对象直投，但与形参 {parameter.ParameterType.Name} 不兼容");

        var text = raw as string ?? raw?.ToString() ?? string.Empty;

        if (arg.Convert == "string") return text;
        if (arg.Convert == "csv") return PortValues.SplitCsv(text).ToList();

        if (PortValues.TryConvertScalar(arg.Convert, text, out var scalar, out var scalarExpected))
            return scalar;
        if (scalarExpected.Length > 0)
            throw Mismatch(portKey, text, index, scalarExpected);

        throw Mismatch(portKey, arg.Convert, index, PortImplVocabulary.RenderList(PortImplVocabulary.CommonConversionEnum));
    }

    /// <summary>结构化直传：取具名实参 <c>with</c> 的原始 JSON 并按目标形参类型反序列化。</summary>
    private static object? FromWith(string portKey, ParameterInfo parameter, PortArgStandard arg, IPortInput input)
    {
        if (arg.From is { Length: > 0 } source && !string.Equals(source, "with", StringComparison.Ordinal))
            throw new PortBindingViolationException(
                portKey, $"端口 '{portKey}' 的 json 实参 from '{source}' 未知（仅 with = 具名实参）");

        if (string.IsNullOrWhiteSpace(arg.Key))
            throw new PortBindingViolationException(portKey, $"端口 '{portKey}' 的 json 实参缺少 key");

        if (!input.With.TryGetValue(arg.Key!, out var value) || value is null)
        {
            if (!arg.Optional)
                throw new PortBindingViolationException(portKey, $"端口 '{portKey}' 缺具名实参 '{arg.Key}'（with）");

            // 缺参口径与位置实参分支同源：声明 default → 按目标形参类型还原；否则取目标形参默认值（D1）。
            return arg.Default is { } fallback
                ? ReflectivePortResolver.FromJson(fallback, parameter.ParameterType)
                : ReflectivePortResolver.DefaultFor(parameter);
        }

        return ReflectivePortResolver.FromJson(value, parameter.ParameterType);
    }

    private static PortBindingViolationException Mismatch(string portKey, string text, int index, string expected)
        => new(portKey, $"端口 '{portKey}' 第 {index} 个实参 '{text}' 不是{expected}");

    /// <summary>声明文本（诊断用；位序即语义）。</summary>
    private static string DeclaredText(IReadOnlyList<PortArgStandard> args)
        => string.Join(",", args.Select(a => a.Convert));
}

/// <summary>
/// 组合族**调用**的解算结果（R3-2）：构造实参 → 执行（静态 / 实例 / 成员读取）→ 期望异常归一 → 异步折取。
/// <para>原 <c>ComposedBinding.ResolvedCall</c> 逐字迁入：组合族调用语义的唯一实现点。</para>
/// </summary>
internal sealed class ComposedCall
{
    private readonly string _portKey;
    private readonly Type? _expect;
    private readonly MethodInfo? _method;
    private readonly MemberInfo? _member;

    internal ComposedCall(
        string portKey, CallStandard spec, int? depIndex, Type? expect,
        MethodInfo? method = null, MemberInfo? member = null)
    {
        _portKey = portKey;
        Spec = spec;
        DepIndex = depIndex;
        _expect = expect;
        _method = method;
        _member = member;
    }

    internal CallStandard Spec { get; }

    /// <summary>实例调用的依赖下标（null = 静态调用）。</summary>
    internal int? DepIndex { get; }

    internal object? Invoke(IPortInput input, object?[] deps)
        => PortInvocation.Invoke(() => Execute(input, deps), _expect is null ? null : new[] { _expect });

    private object? Execute(IPortInput input, object?[] deps)
    {
        var instance = DepIndex is { } index ? deps[index] : null;

        if (_method is not null)
        {
            var args = ComposedArgs.Build(
                _method.GetParameters(), Spec.Args, input, deps, _portKey, $"call '{Spec.As}'");
            return _method.Invoke(instance, args);
        }

        if (_member is not { } member)
            throw new PortBindingViolationException(
                _portKey, $"端口 '{_portKey}' 的调用 '{Spec.As}' 未解析出成员（声明与解析不一致）");

        return ReflectivePortResolver.ReadMember(member, instance);
    }
}

/// <summary>
/// 组合族调用解算（R3-2）：按声明解析目标成员（静态取 <c>target</c>；实例取依赖的 <c>contract</c>/<c>new</c> 类型）。
/// <para>原 <c>ComposedBinding.Resolve / ResolveCallType</c> 逐字迁入；诊断文案不变（RF-5）。</para>
/// </summary>
internal static class PortComposedCall
{
    internal static ComposedCall Resolve(IReadOnlyList<DepStandard> deps, string portKey, CallStandard call)
    {
        if (string.IsNullOrWhiteSpace(call.Method))
            throw new PortBindingViolationException(
                portKey, $"端口 '{portKey}' 的调用 '{call.As}' 缺少 method");

        var type = ResolveCallType(deps, portKey, call, out var depIndex);
        var isStatic = depIndex is null;
        var expect = call.Expect is { Length: > 0 } expectName
            ? ReflectivePortResolver.ResolveType(expectName)
            : null;

        if (call.IsProperty)
            return new ComposedCall(
                portKey, call, depIndex, expect,
                member: ReflectivePortResolver.ResolveMember(type, portKey, call.Method, isStatic));

        return new ComposedCall(
            portKey, call, depIndex, expect,
            method: ReflectivePortResolver.ResolveMethod(
                type, portKey, call.Method, call.Args?.Count ?? 0, isStatic));
    }

    /// <summary>调用所属类型：静态调用取 <c>target</c>；实例调用取依赖的 <c>contract</c> / <c>new</c> 类型。</summary>
    internal static Type ResolveCallType(
        IReadOnlyList<DepStandard> deps, string portKey, CallStandard call, out int? depIndex)
    {
        depIndex = call.Dep;
        if (call.Dep is not { } index)
        {
            if (string.IsNullOrWhiteSpace(call.Target))
                throw new PortBindingViolationException(
                    portKey, $"端口 '{portKey}' 的调用 '{call.As}' 缺少 target（静态调用）或 dep（实例调用）");

            return ReflectivePortResolver.ResolveType(call.Target!);
        }

        if (index < 0 || index >= deps.Count)
            throw new PortBindingViolationException(
                portKey,
                $"端口 '{portKey}' 的调用 '{call.As}' 引用了不存在的依赖下标 {index}（依赖 {deps.Count} 个）");

        return ComposedDeps.DeclaredInstanceType(deps, index, portKey, $"调用 '{call.As}'");
    }
}
