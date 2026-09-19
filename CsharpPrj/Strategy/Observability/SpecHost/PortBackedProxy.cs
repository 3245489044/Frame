using System.Reflection;
using System.Text.Json;
using Strategy.Observability.Diagnostics;
using Strategy.Observability.Ports;

namespace Strategy.Observability.SpecHost;

/// <summary>
/// 端口背书接口代理（P4-0，通用端口框架补件）：把「依赖接口」的类型化对象面接到端口目录上——
/// 方法名映射为端口键 <c>&lt;类型&gt;.&lt;方法&gt;</c>（方法级键约定），每次方法调用即一次端口调用，
/// 故调用记录与 <c>given ports</c> 替身接管对依赖调用同样成立（对拍守卫「端口侧须有调用记录」判据覆盖依赖面）。
/// <para>
/// 存在理由：族改写要求端口实现「从端口目录取依赖对象」，而内核只提供 <see cref="IPortBinding"/>
/// （可调用绑定），无类型化对象面——本件补齐该缝，且**零内核改动**（不触发 Frame 重烘焙）。
/// </para>
/// <para>
/// 边界：只代理**接口**；属性/事件访问器与泛型方法不在端口面（出现即抛可定位异常，禁静默）。
/// 依赖方法未登记端口键 → 抛 <see cref="PortNotRegisteredException"/>（含端口键归因，防拼写漂移静默）。
/// </para>
/// <para>
/// 归位批 I2（宿主上移）：异常类型由测试侧 <c>SpecRunException</c> 换为内核 E14 诊断族
/// （<see cref="PortBindingViolationException"/> / <see cref="PortInputTypeException"/>），
/// 故本件零测试框架依赖。
/// </para>
/// </summary>
public static class PortBackedProxy
{
    /// <summary>
    /// 生成依赖接口 <typeparamref name="T"/> 的代理：方法 <c>M</c> → 端口键 <c>&lt;类型名&gt;.M</c>。
    /// <paramref name="typeName"/> 缺省 = <c>typeof(T).Name</c>（如 <c>IHierarchyReferenceManager</c>）。
    /// <para>调用期解析（非创建期快照）：目录绑定被 <c>given ports</c> 替换后，后续调用即走替身。</para>
    /// </summary>
    public static T Create<T>(IPortCatalog catalog, string? typeName = null) where T : class
        => PortBackedProxyDispatch.Create<T>(catalog, typeName);
}

/// <summary>代理分派（<see cref="DispatchProxy"/>）：本地方法调用 → 端口解析 → 绑定调用 → 目标类型还原。</summary>
/// <remarks>不可为 <c>sealed</c>：<see cref="DispatchProxy.Create{T,TProxy}"/> 生成的代理须继承本类。</remarks>
internal class PortBackedProxyDispatch : DispatchProxy
{
    private IPortCatalog? _catalog;
    private string _typeName = string.Empty;

    /// <summary>生成代理实例（<see cref="PortBackedProxy.Create{T}"/> 的唯一实现点）。</summary>
    internal static T Create<T>(IPortCatalog catalog, string? typeName) where T : class
    {
        ArgumentNullException.ThrowIfNull(catalog);
        if (!typeof(T).IsInterface)
            throw new PortBindingViolationException(
                typeof(T).FullName ?? typeof(T).Name, "端口背书代理只支持接口");

        var proxy = DispatchProxy.Create<T, PortBackedProxyDispatch>();
        var dispatch = (PortBackedProxyDispatch)(object)proxy;
        dispatch._catalog = catalog;
        dispatch._typeName = string.IsNullOrWhiteSpace(typeName) ? typeof(T).Name : typeName;
        return proxy;
    }


    /// <summary>方法调用分派（<c>object</c> 成员本地应答，不落端口面；其余按端口键解析调用）。</summary>
    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
    {
        ArgumentNullException.ThrowIfNull(targetMethod);

        var catalog = _catalog
            ?? throw new PortBindingViolationException(_typeName, "端口背书代理未绑定端口目录（代理构造异常）");

        var callArgs = args ?? [];

        switch (targetMethod.Name)
        {
            case nameof(ToString) when callArgs.Length == 0:
                return $"PortBackedProxy({_typeName})";

            case nameof(GetHashCode) when callArgs.Length == 0:
                return _typeName.GetHashCode(StringComparison.Ordinal);

            case nameof(Equals) when callArgs.Length == 1:
                return ReferenceEquals(this, callArgs[0]);
        }

        if (targetMethod.IsSpecialName)
            throw new PortBindingViolationException(
                _typeName + "." + MemberName(targetMethod.Name),
                $"依赖端口 '{_typeName}.{MemberName(targetMethod.Name)}' 是属性/事件访问器，不在端口面（端口契约只声明方法）");

        if (targetMethod.IsGenericMethod)
            throw new PortBindingViolationException(
                _typeName, $"依赖接口 '{_typeName}' 的泛型方法 '{targetMethod.Name}' 不受支持（端口键须可静态确定）");

        var portKey = _typeName + "." + targetMethod.Name;
        var (portArgs, ct) = SplitArgs(targetMethod, callArgs);

        if (!catalog.TryResolve(portKey, out var handle))
            throw new PortNotRegisteredException(portKey, catalog.Contracts.Select(contract => contract.Key));

        var input = new PortInput(portKey, portArgs);
        var result = handle.Binding.InvokeAsync(input, ct).AsTask().GetAwaiter().GetResult();

        return Adapt(result, targetMethod.ReturnType, portKey);
    }

    /// <summary>
    /// 实参拆分：<see cref="CancellationToken"/> 是宿主控制面，不进端口实参（键面与替身应答不受其影响），
    /// 但透传给绑定调用；其余实参按声明序即端口实参（无手写编码）。
    /// </summary>
    private static (IReadOnlyList<object?> Args, CancellationToken Ct) SplitArgs(MethodInfo method, object?[] callArgs)
    {
        var parameters = method.GetParameters();
        var ct = CancellationToken.None;
        var portArgs = new List<object?>(callArgs.Length);

        for (var i = 0; i < callArgs.Length; i++)
        {
            if (i < parameters.Length && parameters[i].ParameterType == typeof(CancellationToken))
            {
                if (callArgs[i] is CancellationToken token) ct = token;
                continue;
            }

            portArgs.Add(callArgs[i]);
        }

        return (portArgs, ct);
    }

    /// <summary>访问器名 → 成员名（<c>get_Name</c> → <c>Name</c>）：报错须给端口键形态，便于对账声明面。</summary>
    private static string MemberName(string accessorName)
    {
        var separator = accessorName.IndexOf('_');
        return separator >= 0 && separator < accessorName.Length - 1 ? accessorName[(separator + 1)..] : accessorName;
    }

    /// <summary>端口应答 → 目标返回类型（<c>void</c> / <c>Task</c> / <c>ValueTask</c> / <c>Task&lt;T&gt;</c> / <c>ValueTask&lt;T&gt;</c> / 同步值）。</summary>
    private static object? Adapt(object? result, Type returnType, string portKey)
    {
        if (returnType == typeof(void)) return null;
        if (returnType == typeof(Task)) return Task.CompletedTask;
        if (returnType == typeof(ValueTask)) return ValueTask.CompletedTask;

        if (returnType.IsGenericType)
        {
            var definition = returnType.GetGenericTypeDefinition();
            var inner = returnType.GetGenericArguments()[0];

            if (definition == typeof(Task<>))
                return typeof(Task)
                    .GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(inner)
                    .Invoke(null, [Coerce(result, inner, portKey)]);

            if (definition == typeof(ValueTask<>))
                return Activator.CreateInstance(returnType, Coerce(result, inner, portKey));
        }

        return Coerce(result, returnType, portKey);
    }

    /// <summary>
    /// 应答值 → 目标类型：可赋值原样返回；<see cref="JsonElement"/>（<c>fixed</c>/<c>sequence</c> 种子）按目标类型反序列化；
    /// 引用类型 null 原样、值类型 null → 默认值；其余抛可定位异常（禁止静默裁剪应答）。
    /// </summary>
    private static object? Coerce(object? result, Type target, string portKey)
    {
        if (result is null)
            return target.IsValueType && Nullable.GetUnderlyingType(target) is null
                ? Activator.CreateInstance(target)
                : null;

        if (target.IsInstanceOfType(result)) return result;

        if (result is JsonElement seed)
        {
            try
            {
                return JsonSerializer.Deserialize(seed.GetRawText(), target);
            }
            catch (JsonException ex)
            {
                throw new PortInputTypeException(
                    portKey, $"依赖端口的应答无法还原为 {target.Name}（种子与目标类型不匹配）：{ex.Message}");
            }
        }

        if (target == typeof(string)) return result.ToString();

        throw new PortInputTypeException(
            portKey, $"依赖端口的应答类型 {result.GetType().Name} 与目标类型 {target.Name} 不匹配");
    }
}
