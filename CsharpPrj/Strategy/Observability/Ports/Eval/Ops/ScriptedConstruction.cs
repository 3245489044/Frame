using System.Reflection;
using Strategy.Observability.Diagnostics;

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
        ScriptedNewSpec spec, EvalScope scope, IPortInput input, object?[] deps, string context)
    {
        if (string.IsNullOrWhiteSpace(spec.Type))
            throw new PortBindingViolationException(
                scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 缺 type（构造类型全名）");

        var type = ComposedDeps.ResolveType(spec.Type, spec.GenericArgs);
        var declared = spec.Args ?? Array.Empty<ScriptedArgSpec>();
        var parameters = ComposedDeps
            .PickConstructor(
                scope.PortKey, type, declared.Count, context,
                constructor => ScriptedInvokeRunner.TryBind(
                    scope.PortKey, $"{context}（{type.Name}）", declared,
                    constructor.GetParameters(), scope, input, deps),
                ScriptedValue.DescribeArgs(declared))
            .GetParameters();

        var args = ScriptedInvokeRunner.Bind(
            scope.PortKey, $"{context}（{type.Name}）", declared, parameters, scope, input, deps);

        var value = Activator.CreateInstance(type, args);

        if (spec.Init is { Count: > 0 } init) ApplyInit(init, value, scope, input, deps, context);

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

    /// <summary>
    /// 对象初始化器求值（W13）：逐项按成员名赋值（属性优先、回落字段；<c>init</c> 访问器同样可赋值）；
    /// 成员缺失 / 缺 <c>value</c> / 无可赋值实例 → 即抛。声明序 = 赋值序。
    /// </summary>
    private static void ApplyInit(
        IReadOnlyList<ScriptedInitSpec> init, object? instance,
        EvalScope scope, IPortInput input, object?[] deps, string context)
    {
        if (instance is null)
            throw new PortBindingViolationException(
                scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 的 init 无可赋值实例（构造返回 null）");

        foreach (var item in init)
        {
            if (string.IsNullOrWhiteSpace(item.Member))
                throw new PortBindingViolationException(
                    scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 的 init 项缺 member（成员名）");

            if (item.Value is null)
                throw new PortBindingViolationException(
                    scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 的 init 成员 '{item.Member}' 缺 value（取值声明）");

            var descriptor = ReflectivePortResolver.ResolveMember(
                instance.GetType(), scope.PortKey, item.Member, isStatic: false);

            switch (descriptor)
            {
                case PropertyInfo property:
                    property.SetValue(instance, ScriptedValue.ResolveTyped(
                        item.Value, property.PropertyType, scope, input, deps,
                        $"{context} 的 init 成员 '{item.Member}'"));
                    break;

                case FieldInfo field:
                    field.SetValue(instance, ScriptedValue.ResolveTyped(
                        item.Value, field.FieldType, scope, input, deps,
                        $"{context} 的 init 成员 '{item.Member}'"));
                    break;

                default:
                    throw new PortBindingViolationException(
                        scope.PortKey,
                        $"端口 '{scope.PortKey}' 的 {context} 的 init 成员 '{item.Member}' 不是属性 / 字段（不可赋值）");
            }
        }
    }
}
