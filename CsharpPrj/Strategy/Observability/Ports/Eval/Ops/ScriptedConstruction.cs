using System.Collections;
using System.Reflection;
using Strategy.Observability.Diagnostics;

namespace Strategy.Observability.Ports;

using DataCenter.Protocol.Standards;

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
        NewStandard spec, EvalScope scope, IPortInput input, object?[] deps, string context)
    {
        if (string.IsNullOrWhiteSpace(spec.Type))
            throw new PortBindingViolationException(
                scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 缺 type（构造类型全名）");

        var type = ComposedDeps.ResolveType(spec.Type, spec.GenericArgs);
        var declared = spec.Args ?? Array.Empty<ValueSourceStandard>();
        var constructor = ComposedDeps.PickConstructor(
            scope.PortKey, type, declared.Count, context,
            candidate => ScriptedInvokeRunner.Bind(
                scope.PortKey, $"{context}（{type.Name}）", declared,
                candidate.GetParameters(), scope, input, deps),
            ScriptedValue.DescribeArgs(declared),
            namedBinding: declared.Any(arg => arg.Named is { Length: > 0 }));
        var parameters = constructor.GetParameters();

        var args = ScriptedInvokeRunner.Bind(
            scope.PortKey, $"{context}（{type.Name}{ScriptedInvokeRunner.Signature(parameters)}）",
            declared, parameters, scope, input, deps);

        // 构造期期望异常（`new.expect`）：口径与 `invoke.expect` **同源**（PortInvocation.ExecuteAny）——
        // 命中 ⇒ 结果 = 异常对象本身（构造未发生 ⇒ 不施加 init / chain）；未命中 / 未抛 ⇒ 结果为构造实例。
        if (spec.Expect is { Length: > 0 } expected)
        {
            var captured = PortInvocation.ExecuteAny(
                () => PortInvocation.Construct(type, args), ScriptedInvokeRunner.ExpectTypes(expected));

            if (captured is Exception) return captured;

            return Finish(captured);
        }

        return Finish(PortInvocation.Construct(type, args));

        // 构造后置处理（init 赋值 → chain 链式调用）单源：期望异常命中时不经此路（结果即异常对象）。
        object? Finish(object? instance)
        {
            if (spec.Init is { Count: > 0 } init) ApplyInit(init, instance, scope, input, deps, context);

            if (spec.Chain is not { Count: > 0 } chain) return instance;

            var current = instance;
            foreach (var link in chain)
            {
                if (current is null)
                    throw new PortBindingViolationException(
                        scope.PortKey, $"端口 '{scope.PortKey}' 的 {context} 的链式调用 '{link.Method}' 无可调用实例");

                var call = new InvokeStandard(Method: link.Method, Args: link.Args);
                var result = ScriptedInvokeRunner.RunOn(scope.PortKey, call, current, scope, input, deps).Result;

                if (link.Rebind) current = result;
            }

            return current;
        }
    }

    /// <summary>
    /// 对象初始化器求值（W13）：逐项按成员名赋值（属性优先、回落字段；<c>init</c> 访问器同样可赋值）；
    /// 成员缺失 / 缺 <c>value</c> / 无可赋值实例 → 即抛。声明序 = 赋值序。
    /// <para>G7：项给出 <c>key</c> 时改为**容器写入**（<c>member[key] = value</c>）。</para>
    /// </summary>
    private static void ApplyInit(
        IReadOnlyList<InitStandard> init, object? instance,
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

            var label = $"{context} 的 init 成员 '{item.Member}'";

            if (item.Key is { } keySource)
            {
                SetEntry(instance, descriptor, item, keySource, scope, input, deps, label);
                continue;
            }

            switch (descriptor)
            {
                case PropertyInfo property:
                    property.SetValue(instance, ScriptedValue.ResolveTyped(
                        item.Value, property.PropertyType, scope, input, deps, label));
                    break;

                case FieldInfo field:
                    field.SetValue(instance, ScriptedValue.ResolveTyped(
                        item.Value, field.FieldType, scope, input, deps, label));
                    break;

                default:
                    throw new PortBindingViolationException(
                        scope.PortKey,
                        $"端口 '{scope.PortKey}' 的 {label} 不是属性 / 字段（不可赋值）");
            }
        }
    }

    /// <summary>
    /// 容器写入（G7：<c>member[key] = value</c>）：成员取值须为字典（<see cref="IDictionary"/>）或
    /// 列表（<see cref="IList"/>）——键与值按容器的键 / 元素类型做类型定向转换；取值为 null /
    /// 键为 null / 不可写容器 / 下标越界 → 即抛（禁静默丢弃）。
    /// </summary>
    private static void SetEntry(
        object instance, MemberInfo member, InitStandard item, ValueSourceStandard keySource,
        EvalScope scope, IPortInput input, object?[] deps, string label)
    {
        var container = ReflectivePortResolver.ReadMember(member, instance)
            ?? throw new PortBindingViolationException(
                scope.PortKey, $"端口 '{scope.PortKey}' 的 {label} 取值为 null（容器写入需已初始化容器）");

        var (keyType, valueType) = EntryTypes(container.GetType());

        var key = ScriptedValue.ResolveTyped(keySource, keyType, scope, input, deps, $"{label} 的 key");

        switch (container)
        {
            case IDictionary dictionary:
                if (key is null)
                    throw new PortBindingViolationException(
                        scope.PortKey, $"端口 '{scope.PortKey}' 的 {label} 的 key 求值为 null（字典键不得为空）");

                dictionary[key] = ScriptedValue.ResolveTyped(item.Value!, valueType, scope, input, deps, label);
                return;

            case IList list:
                if (key is not int position)
                    throw new PortBindingViolationException(
                        scope.PortKey,
                        $"端口 '{scope.PortKey}' 的 {label} 的 key 须为整型下标（列表写入；实际 {keyType.Name}）");

                if (position < 0 || position >= list.Count)
                    throw new PortBindingViolationException(
                        scope.PortKey,
                        $"端口 '{scope.PortKey}' 的 {label} 的下标 {position} 越界（列表长度 {list.Count}）");

                list[position] = ScriptedValue.ResolveTyped(item.Value!, valueType, scope, input, deps, label);
                return;

            default:
                throw new PortBindingViolationException(
                    scope.PortKey,
                    $"端口 '{scope.PortKey}' 的 {label} 取值类型 {container.GetType().Name} 不支持按 key 写入" +
                    "（须为 IDictionary / IList）");
        }
    }

    /// <summary>容器的键 / 值类型（泛型实参可用时取其精确类型；否则按 <see cref="object"/> 原样传递）。</summary>
    private static (Type Key, Type Value) EntryTypes(Type container)
    {
        if (container.IsGenericType)
        {
            var arguments = container.GetGenericArguments();

            if (arguments.Length == 2 && typeof(IDictionary).IsAssignableFrom(container))
                return (arguments[0], arguments[1]);

            if (arguments.Length == 1 && typeof(IList).IsAssignableFrom(container))
                return (typeof(int), arguments[0]);
        }

        return (typeof(object), typeof(object));
    }
}
