using System.Reflection;

namespace Strategy.Observability.Ports;

/// <summary>
/// **对象图构造单源**（W6）：解析类型（含泛型闭合）→ 构实参 → 反射构造 → 链式后置调用。
/// <para>三处消费共用同一实现：步骤 <c>new</c>、声明链式 <c>chain</c>、取值来源 <c>from:new</c>（实参内嵌构造，
/// 配合 <c>convert:array</c> 即得「嵌套 new + Add 链 + 数组形参」的对象图）。</para>
/// <para>零领域词汇：类型名 / 方法名一律不透明字符串（L1 红线）。</para>
/// </summary>
internal static class ScriptedConstruction
{
    /// <summary>构造并（可选）链式后置调用；<paramref name="context"/> 仅用于诊断定位。</summary>
    internal static object? Build(
        ScriptedNewSpec spec, ScriptedScope scope, IPortInput input, object?[] deps, string context)
    {
        if (string.IsNullOrWhiteSpace(spec.Type))
            throw new PortBindingViolationException(
                scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 缺 type（构造类型全名）");

        var type = ComposedDeps.ResolveType(spec.Type, spec.GenericArgs);
        var declared = spec.Args ?? Array.Empty<ScriptedArgSpec>();
        var parameters = ComposedDeps
            .PickConstructor(scope.PortKey, type, declared.Count, context)
            .GetParameters();

        var args = new object?[parameters.Length];
        for (var i = 0; i < parameters.Length; i++)
            args[i] = i < declared.Count
                ? ScriptedValue.ResolveTyped(
                    declared[i], parameters[i].ParameterType, scope, input, deps, $"{context} 第 {i} 个实参")
                : ReflectivePortResolver.DefaultFor(parameters[i].ParameterType);

        var value = Activator.CreateInstance(type, args);
        if (spec.Chain is not { Count: > 0 } chain) return value;

        foreach (var link in chain)
        {
            if (value is null)
                throw new PortBindingViolationException(
                    scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 的链式调用 '{link.Method}' 无可调用实例");

            var call = new ScriptedInvokeSpec(Method: link.Method, Args: link.Args);
            var result = ScriptedInvokeRunner.RunOn(scope.PortKey, call, value, scope, input, deps).Result;

            if (link.Rebind) value = result;
        }

        return value;
    }
}
