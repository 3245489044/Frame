namespace Strategy.Observability.Ports;

using System.Globalization;
using System.Reflection;
using Strategy.Observability.Diagnostics;

/// <summary>
/// 调用算子（R3-1）：解析目标成员 → 逐位实参绑定（唯一转换表）→ 调用归一。
/// <para>
/// **唯一实现点**：反射绑定（<see cref="ReflectiveBinding"/>）转薄适配后与本算子共用同一份代码，
/// 消除「绑定类各持一份解析/转换」的漂移面（D-2）；诊断文案与迁移前逐字一致（RF-5）。
/// </para>
/// </summary>
internal static class PortCall
{
    /// <summary>声明 → 目标方法（构造期 fail-fast：缺 <c>target/method</c> 或不可解析即抛）。</summary>
    internal static MethodInfo Resolve(PortBindingSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        if (string.IsNullOrWhiteSpace(spec.Target) || string.IsNullOrWhiteSpace(spec.Method))
            throw new PortBindingViolationException(
                spec.PortKey, $"端口 '{spec.PortKey}' 声明 reflective 绑定但缺少 target/method");

        return ReflectivePortResolver.ResolveMethod(spec, ReflectivePortResolver.ResolveType(spec.Target));
    }

    /// <summary>按声明转换位置实参；未声明槽位（如尾部注入参数）取该类型默认值。</summary>
    internal static object?[] BuildArguments(PortBindingSpec spec, MethodInfo method, IPortInput input)
    {
        ArgumentNullException.ThrowIfNull(spec);
        ArgumentNullException.ThrowIfNull(method);
        ArgumentNullException.ThrowIfNull(input);

        var parameters = method.GetParameters();
        var args = new object?[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
            args[i] = i < spec.Args.Count
                ? Convert(spec, parameters[i], spec.Args[i], input, i)
                : ReflectivePortResolver.DefaultFor(parameters[i].ParameterType);

        return args;
    }

    /// <summary>调用目标方法；业务异常原样上抛（错误分类交调用记录）。</summary>
    internal static object? Invoke(MethodInfo method, object?[] arguments)
    {
        try
        {
            return method.Invoke(null, arguments);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is { } inner)
        {
            return PortInvocation.ExpectAny(inner, expect: null);
        }
    }

    private static object? Convert(PortBindingSpec spec, ParameterInfo parameter, PortArgSpec arg, IPortInput input, int index)
    {
        if (string.Equals(arg.Convert, "json", StringComparison.Ordinal))
            return FromWith(spec, parameter, arg, input);

        var present = index < input.Args.Count && input.Args[index] is not null;
        if (!present)
        {
            if (!arg.Optional)
                throw new PortBindingViolationException(
                    spec.PortKey,
                    $"{spec.PortKey} 需要第 {index} 个位置实参（声明 {DeclaredText(spec)}），实际只有 {input.Args.Count} 个");

            return arg.Default is { } fallback
                ? ReflectivePortResolver.FromJson(fallback, parameter.ParameterType)
                : ReflectivePortResolver.DefaultFor(parameter.ParameterType);
        }

        var raw = input.Args[index];
        if (arg.NullLiteral && raw is string literal && string.Equals(literal, "null", StringComparison.Ordinal)) return null;

        if (PortValues.IsDirectPositional(arg.Convert, raw))
            return PortValues.FitsDirect(raw, parameter.ParameterType)
                ? raw
                : throw new PortBindingViolationException(
                    spec.PortKey,
                    $"{spec.PortKey} 第 {index} 个位置实参按对象直投，但与形参 {parameter.ParameterType.Name} 不兼容");

        var text = raw as string ?? raw?.ToString() ?? string.Empty;

        return arg.Convert switch
        {
            "string" => text,
            "csv" => PortValues.SplitCsv(text).ToList(),
            "bool" => ParseBool(spec, text, index),
            "int" => int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var intValue)
                ? intValue
                : throw Mismatch(spec, text, index, "整数"),
            "double" => double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var doubleValue)
                ? doubleValue
                : throw Mismatch(spec, text, index, "数值"),
            "json" => FromWith(spec, parameter, arg, input),
            _ => throw Mismatch(spec, arg.Convert, index, "string|bool|int|double|csv|json|object"),
        };
    }

    /// <summary>结构化直传：取具名实参 <c>with</c> 的原始 JSON 并按目标形参类型反序列化。</summary>
    private static object? FromWith(PortBindingSpec spec, ParameterInfo parameter, PortArgSpec arg, IPortInput input)
    {
        if (arg.From is { Length: > 0 } source && !string.Equals(source, "with", StringComparison.Ordinal))
            throw new PortBindingViolationException(
                spec.PortKey,
                $"端口 '{spec.PortKey}' 的 json 实参 from '{source}' 未知（仅 with = 具名实参）");

        if (string.IsNullOrWhiteSpace(arg.Key))
            throw new PortBindingViolationException(
                spec.PortKey, $"端口 '{spec.PortKey}' 的 json 实参缺少 key");

        if (!input.With.TryGetValue(arg.Key!, out var value) || value is null)
        {
            if (!arg.Optional)
                throw new PortBindingViolationException(
                    spec.PortKey, $"端口 '{spec.PortKey}' 缺具名实参 '{arg.Key}'（with）");

            // 缺参口径与位置实参分支同源：声明 default → 按目标形参类型还原；否则取目标类型默认值。
            return arg.Default is { } fallback
                ? ReflectivePortResolver.FromJson(fallback, parameter.ParameterType)
                : ReflectivePortResolver.DefaultFor(parameter.ParameterType);
        }

        return ReflectivePortResolver.FromJson(value, parameter.ParameterType);
    }

    private static bool ParseBool(PortBindingSpec spec, string text, int index)
        => bool.TryParse(text, out var value)
            ? value
            : text is "1" or "0"
                ? text == "1"
                : throw Mismatch(spec, text, index, "布尔");

    private static PortBindingViolationException Mismatch(PortBindingSpec spec, string text, int index, string expected)
        => new(spec.PortKey, $"端口 '{spec.PortKey}' 第 {index} 个实参 '{text}' 不是{expected}");

    /// <summary>声明文本（诊断用；位序即语义）。</summary>
    private static string DeclaredText(PortBindingSpec spec) => string.Join(",", spec.Args.Select(a => a.Convert));
}
