using System.Globalization;
using System.Reflection;
using System.Text.Json;
using Strategy.Observability.Diagnostics;

namespace Strategy.Observability.Ports;

/// <summary>
/// 组合绑定的实参构造小件（构造实参 / 工厂实参 / 调用实参共用同一口径，避免三处漂移）：
/// 取值来源 = 用例位置实参 / 具名实参（<c>with</c>）/ 依赖引用 / 常量 JSON；按目标形参类型转换。
/// </summary>
internal static class ComposedArgs
{
    /// <summary>按目标形参表构造实参数组（声明不足 → 该形参类型默认值，与反射绑定同口径）。</summary>
    internal static object?[] Build(
        ParameterInfo[] parameters, IReadOnlyList<ComposedArgSpec>? declared,
        IPortInput input, object?[] deps, string portKey, string context)
    {
        var count = declared?.Count ?? 0;
        var args = new object?[parameters.Length];

        for (var i = 0; i < parameters.Length; i++)
            args[i] = i < count
                ? Convert(parameters[i], declared![i], input, deps, portKey, context, i)
                : ReflectivePortResolver.DefaultFor(parameters[i].ParameterType);

        return args;
    }

    /// <summary>声明实参摘要（诊断文案：构造择优的「实参 […]」段）。</summary>
    internal static string Describe(IReadOnlyList<ComposedArgSpec>? declared)
        => declared is not { Count: > 0 }
            ? "(无)"
            : string.Join(", ", declared.Select(spec => spec.Value is { } value
                ? $"value {value.GetRawText()}"
                : spec.ArgIndex is { } index ? $"arg[{index}]"
                : spec.Key is { Length: > 0 } key ? $"with '{key}'"
                : spec.Dep is { } dep ? $"dep[{dep}]"
                : "optional"));

    /// <summary>按声明序求值（工厂实参：工厂签名自定，不做形参类型转换，值按原义传递）。</summary>
    internal static IReadOnlyList<object?> Values(
        IReadOnlyList<ComposedArgSpec>? declared, IPortInput input, object?[] deps, string portKey, string context)
        => declared is not { Count: > 0 }
            ? Array.Empty<object?>()
            : declared.Select((spec, index) => Value(spec, input, deps, portKey, context, index)).ToArray();

    /// <summary>无类型目标的取值（工厂实参用）。</summary>
    internal static object? Value(
        ComposedArgSpec spec, IPortInput input, object?[] deps, string portKey, string context, int index)
    {
        if (spec.Dep is { } reference) return Dep(deps, reference, portKey, context, index);
        if (spec.ArgIndex is { } position) return PositionalRaw(input, spec, position, portKey, context);
        if (spec.Key is { Length: > 0 } key) return WithRaw(input, key, portKey, context, spec.Optional);
        if (spec.Value is { } constant) return Unwrap(constant);
        if (spec.Optional) return null;

        throw new PortBindingViolationException(
            portKey, $"端口 '{portKey}' 的 {context} 第 {index} 个实参未声明取值来源（argIndex|key|dep|value|optional）");
    }

    /// <summary>按目标形参类型转换（位序即语义；无值分支由 <c>optional</c> 决定默认值或 fail-fast）。</summary>
    private static object? Convert(
        ParameterInfo parameter, ComposedArgSpec spec,
        IPortInput input, object?[] deps, string portKey, string context, int index)
    {
        var target = parameter.ParameterType;

        if (spec.Dep is { } reference)
        {
            var dep = Dep(deps, reference, portKey, context, index);
            if (dep is not null && !target.IsInstanceOfType(dep))
                throw new PortBindingViolationException(
                    portKey, $"端口 '{portKey}' 的 {context} 第 {index} 个实参：依赖对象的类型与形参 {target.Name} 不匹配");

            return dep;
        }

        if (spec.Value is { } constant)
            return Coerce(constant, target);

        if (spec.ArgIndex is { } position)
            return FromPositional(target, spec, input, position, portKey, context);

        if (spec.Key is { Length: > 0 } key)
            return FromWith(target, spec, input, key, portKey, context, index);

        if (spec.Optional)
            return spec.Default is { } fallback
                ? Coerce(fallback, target)
                : None(spec, target);

        throw new PortBindingViolationException(
            portKey, $"端口 '{portKey}' 的 {context} 第 {index} 个实参未声明取值来源（argIndex|key|dep|value|optional）");
    }

    /// <summary>位置实参 → 目标类型（<c>.specs</c> 的 <c>args</c>）。</summary>
    private static object? FromPositional(
        Type target, ComposedArgSpec spec, IPortInput input, int position, string portKey, string context)
    {
        var present = position < input.Args.Count && input.Args[position] is not null;
        if (!present)
        {
            if (!spec.Optional)
                throw new PortBindingViolationException(
                    portKey, $"端口 '{portKey}' 的 {context} 需要第 {position} 个位置实参，实际只有 {input.Args.Count} 个");

            return None(spec, target);
        }

        var raw = input.Args[position];
        if (spec.NullLiteral && raw is string literal && string.Equals(literal, "null", StringComparison.Ordinal)) return null;

        if (PortValues.IsDirectPositional(spec.Convert, raw))
            return PortValues.FitsDirect(raw, target)
                ? raw
                : throw new PortBindingViolationException(
                    portKey, $"端口 '{portKey}' 的 {context} 第 {position} 个位置实参按对象直投，但与目标类型 {target.Name} 不兼容");

        var text = raw as string ?? raw?.ToString() ?? string.Empty;

        return ConvertText(target, spec, text, portKey, context, position);
    }

    /// <summary>具名实参（<c>with</c>）→ 目标类型（<c>json</c>/<c>array</c> 走反序列化；标量按文本口径转换）。</summary>
    private static object? FromWith(
        Type target, ComposedArgSpec spec, IPortInput input, string key, string portKey, string context, int index)
    {
        var raw = WithRaw(input, key, portKey, context, spec.Optional);
        if (raw is null) return None(spec, target);

        if (spec.Convert is "json" or "array") return Coerce(raw, target);
        if (raw is not JsonElement element) return raw;

        var text = element.ValueKind == JsonValueKind.String
            ? element.GetString() ?? string.Empty
            : element.GetRawText();

        return ConvertText(target, spec, text, portKey, context, index);
    }

    /// <summary>文本 → 目标类型（位置实参与具名实参共用的标量口径）。</summary>
    private static object? ConvertText(
        Type target, ComposedArgSpec spec, string text, string portKey, string context, int index)
    {
        if (spec.Convert == "string") return text;
        if (spec.Convert == "csv") return PortValues.SplitCsv(text).ToList();

        if (spec.Convert == "bool")
        {
            if (PortValues.TryNormalize(text, typeof(bool), out var flag) && flag is bool parsed) return parsed;
            throw Mismatch(portKey, context, index, text, "布尔");
        }

        if (spec.Convert == "int")
        {
            if (int.TryParse(text, NumberStyles.Integer, CultureInfo.InvariantCulture, out var number)) return number;
            throw Mismatch(portKey, context, index, text, "整数");
        }

        if (spec.Convert == "double")
        {
            if (double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var real)) return real;
            throw Mismatch(portKey, context, index, text, "数值");
        }

        if (spec.Convert == "json")
            return string.IsNullOrWhiteSpace(text)
                ? None(spec, target)
                : Coerce(JsonDocument.Parse(text).RootElement.Clone(), target);

        throw Mismatch(portKey, context, index, spec.Convert, "string|bool|int|double|csv|json|object");
    }

    /// <summary>无值分支：声明 <c>default</c> → 反序列化；否则取目标类型默认值（数组 → 空数组，与空集语义一致）。</summary>
    private static object? None(ComposedArgSpec spec, Type target)
    {
        if (spec.Default is { } fallback) return Coerce(fallback, target);

        return target.IsArray
            ? Array.CreateInstance(target.GetElementType()!, 0)
            : ReflectivePortResolver.DefaultFor(target);
    }

    /// <summary>JSON 值 → 目标类型（<c>JsonElement</c>/<c>object</c> 位保留元素；null / undefined → null）。</summary>
    private static object? Coerce(object? value, Type target) => PortValues.Coerce(value, target);

    /// <summary>具名实参原始值（缺键 / null：<c>optional</c> → null，否则 fail-fast）。</summary>
    private static object? WithRaw(IPortInput input, string key, string portKey, string context, bool optional)
    {
        if (input.With.TryGetValue(key, out var value) && value is not null) return value;
        if (optional) return null;

        throw new PortBindingViolationException(
            portKey, $"端口 '{portKey}' 的 {context} 缺具名实参 '{key}'（with）");
    }

    /// <summary>依赖引用（下标必须存在；越界 fail-fast）。</summary>
    private static object? Dep(object?[] deps, int reference, string portKey, string context, int index)
        => reference >= 0 && reference < deps.Length
            ? deps[reference]
            : throw new PortBindingViolationException(
                portKey,
                $"端口 '{portKey}' 的 {context} 第 {index} 个实参引用了不可用的依赖下标 {reference}（依赖共 {deps.Length} 个）");

    /// <summary>常量 JSON → 原义值（标量取 CLR 值；结构保留元素）。</summary>
    private static object? Unwrap(JsonElement element) => PortValues.Unwrap(element);

    private static PortBindingViolationException Mismatch(
        string portKey, string context, int index, string text, string expected)
        => new(portKey, $"端口 '{portKey}' 的 {context} 第 {index} 个实参 '{text}' 不是{expected}");

    /// <summary>
    /// 位置实参原值（越界 fail-fast：诊断含实际个数）：对象直投形态（<see cref="PortValues.IsDirectPositional"/>）
    /// 原样返回，其余按 <c>convert</c> 原义文本化（无类型目标的工厂实参用）。
    /// </summary>
    private static object? PositionalRaw(
        IPortInput input, ComposedArgSpec spec, int position, string portKey, string context)
    {
        if (position >= input.Args.Count)
            throw new PortBindingViolationException(
                portKey, $"端口 '{portKey}' 的 {context} 需要第 {position} 个位置实参，实际只有 {input.Args.Count} 个");

        var raw = input.Args[position];
        if (PortValues.IsDirectPositional(spec.Convert, raw)) return raw;

        return raw as string ?? raw?.ToString() ?? string.Empty;
    }
}
