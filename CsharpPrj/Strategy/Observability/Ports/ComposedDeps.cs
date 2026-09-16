using System.Reflection;
using Strategy.Observability.Diagnostics;

namespace Strategy.Observability.Ports;

/// <summary>
/// 依赖构建单源（组合绑定 <c>deps</c> 与脚本绑定 <c>deps</c>/<c>script[].new</c> 共用；E15 续）：
/// <c>new</c>（反射构造，含开放泛型闭合）｜<c>factory</c>（数据半工厂表）｜<c>fake</c>（代理替身）。
/// <para>分工：本件只认不透明字符串 + 反射（L1 红线），领域语义由声明文本承载。</para>
/// </summary>
internal static class ComposedDeps
{
    /// <summary>按声明序构建依赖（下标引用只能指向更早的依赖：保证构建拓扑无环）。</summary>
    internal static object?[] Build(
        IReadOnlyList<ComposedDepSpec> spec, IPortInput input,
        IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>> factories, string portKey)
    {
        var deps = new object?[spec.Count];
        for (var i = 0; i < deps.Length; i++)
            deps[i] = BuildDep(spec, i, input, deps, factories, portKey);

        return deps;
    }

    /// <summary>构建单个依赖（形态三选一：<c>new</c>/<c>factory</c>/<c>fake</c>）。</summary>
    internal static object? BuildDep(
        IReadOnlyList<ComposedDepSpec> spec, int index, IPortInput input, object?[] deps,
        IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>> factories, string portKey)
    {
        var dep = spec[index];
        if (dep.Fake is { Length: > 0 } kind) return BuildFake(spec, index, kind, deps, portKey);

        if (dep.New is { Length: > 0 } typeName)
        {
            var type = ResolveType(typeName, dep.GenericArgs);
            var ctor = PickConstructor(portKey, type, index, dep.Args);
            var args = ComposedArgs.Build(
                ctor.GetParameters(), dep.Args, input, deps, portKey, $"dep[{index}].new {type.Name}");
            return Activator.CreateInstance(type, args);
        }

        if (dep.Factory is { Length: > 0 } factory)
        {
            if (!factories.TryGetValue(factory, out var builder))
                throw new PortBindingViolationException(
                    portKey,
                    $"端口 '{portKey}' 的 dep[{index}] 引用未注入的工厂 '{factory}'" +
                    $"（已注入：{(factories.Count == 0 ? "(无)" : string.Join(", ", factories.Keys))}）");

            return builder(ComposedArgs.Values(dep.Args, input, deps, portKey, $"dep[{index}].factory"));
        }

        throw new PortBindingViolationException(
            portKey, $"端口 '{portKey}' 的 dep[{index}] 未声明形态（须给 new / factory / fake）");
    }

    /// <summary>构建代理替身（计数 / 记录 / 固定应答 / 序列应答 / 不该调用）。</summary>
    internal static object? BuildFake(
        IReadOnlyList<ComposedDepSpec> spec, int index, string kind, object?[] deps, string portKey)
    {
        var dep = spec[index];
        var contract = ContractOf(index, dep, portKey);
        var wrapped = dep.Wrap is { } wrap ? Dep(deps, wrap, index, portKey) : null;

        return kind switch
        {
            "counting" => ProxyFakes.Counting(contract, Wrapped(index, wrapped, kind, portKey), dep.Methods),
            "recording" => ProxyFakes.Recording(contract, Wrapped(index, wrapped, kind, portKey), dep.Methods),
            "fixed" => ProxyFakes.Fixed(
                contract, dep.Values is { Count: > 0 } values ? values[0] : null),
            "sequence" => ProxyFakes.Sequence(
                contract, (dep.Values ?? Array.Empty<System.Text.Json.JsonElement>()).Cast<object?>().ToArray()),
            "notExpected" => ProxyFakes.NotExpected(contract, dep.Methods, wrapped),
            _ => throw new PortBindingViolationException(
                portKey,
                $"端口 '{portKey}' 的 dep[{index}] fake 种类 '{kind}' 未知" +
                "（recording|counting|fixed|sequence|notExpected）"),
        };
    }

    /// <summary>包装形态的被装饰对象（recording/counting 必须给出 wrap；否则无法转发）。</summary>
    private static object Wrapped(int index, object? wrapped, string kind, string portKey)
        => wrapped ?? throw new PortBindingViolationException(
            portKey,
            $"端口 '{portKey}' 的 dep[{index}] 声明 fake='{kind}' 但缺 wrap（该形态需装饰一个已有依赖）");

    /// <summary>替身契约接口（声明必填：不从运行时类型猜接口，避免多接口时静默择一）。</summary>
    private static Type ContractOf(int index, ComposedDepSpec spec, string portKey)
        => spec.Contract is { Length: > 0 } name
            ? ReflectivePortResolver.ResolveType(name)
            : throw new PortBindingViolationException(
                portKey,
                $"端口 '{portKey}' 的 dep[{index}] 声明 fake='{spec.Fake}' 但缺 contract（契约接口全名）");

    /// <summary>依赖引用（下标必须存在且指向更早的依赖）。</summary>
    internal static object? Dep(object?[] deps, int reference, int index, string portKey)
        => reference >= 0 && reference < deps.Length
            ? deps[reference]
            : throw new PortBindingViolationException(
                portKey,
                $"端口 '{portKey}' 的 dep[{index}] 引用了不可用的依赖下标 {reference}" +
                $"（依赖共 {deps.Length} 个，且只能引用更早的依赖）");

    /// <summary>类型解析（含开放泛型闭合：<c>GenericArgs</c> 按序闭合泛型形参）。</summary>
    internal static Type ResolveType(string typeName, IReadOnlyList<string>? genericArgs)
    {
        var type = ReflectivePortResolver.ResolveType(typeName);
        if (genericArgs is not { Count: > 0 }) return type;

        return type.IsGenericTypeDefinition
            ? type.MakeGenericType(genericArgs.Select(ReflectivePortResolver.ResolveType).ToArray())
            : type;
    }

    /// <summary>构造选择：按声明实参个数精确匹配，否则取参数最少者（尾部参数按默认值补齐）。</summary>
    internal static ConstructorInfo PickConstructor(
        string portKey, Type type, int index, IReadOnlyList<ComposedArgSpec>? declared)
        => PickConstructor(portKey, type, declared?.Count ?? 0, $"dep[{index}]");

    /// <summary>构造选择（按声明实参个数；脚本步骤 <c>new</c> 亦复用本单源）。</summary>
    internal static ConstructorInfo PickConstructor(
        string portKey, Type type, int declaredCount, string context)
    {
        var constructors = type.GetConstructors(BindingFlags.Public | BindingFlags.Instance);

        return constructors.FirstOrDefault(c => c.GetParameters().Length == declaredCount)
               ?? constructors.OrderBy(c => c.GetParameters().Length).FirstOrDefault()
               ?? throw new PortBindingViolationException(
                   portKey, $"端口 '{portKey}' 的 {context}：类型 {type.Name} 上没有公开构造函数");
    }
}
