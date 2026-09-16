using System.Reflection;
using Strategy.Observability.Diagnostics;

namespace Strategy.Observability.Ports;

/// <summary>
/// 声明式组合绑定（E15 续 / 消除通道 C1–C4）：把「装配（new/工厂/替身）→ 调用（静态/实例/异步折取/期望异常）→
/// 投影（字段 ← 取值法）」从夹具 C# 变为标准层声明后，由本件按声明执行并产出
/// <c>字段 → 值</c> 字典（与旧夹具 <c>Product(...)</c> 逐字等价）。
/// <para>
/// 定位：与 <see cref="ReflectiveBinding"/> 并列的机制件——同为零领域词汇（类型全名 / 方法名 / 字段名均为
/// 不透明字符串 + 委托），差别仅为「一次直连」对「装配 + 多次调用 + 投影」。
/// </para>
/// <para>
/// 依赖与数据半工厂：<c>new</c> 由本件反射构造；<c>factory</c> 由装配方注入的工厂表解析（L1 不识别领域位置，
/// 工厂名是不透明串）；<c>fake</c> 由 <see cref="ProxyFakes"/> 生成。
/// </para>
/// </summary>
public sealed class ComposedBinding : IPortBinding
{
    private readonly ComposedBindingSpec _spec;
    private readonly IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>> _factories;
    private readonly IReadOnlyList<ResolvedCall> _calls;

    /// <summary>
    /// 按声明构造绑定：调用 / 依赖声明残缺、引用名重复、目标类型或成员不可解析 → 即抛（fail-fast，防静默绑错）。
    /// 数据半工厂表随 <see cref="ComposedBindingSpec.Factories"/> 传递。
    /// </summary>
    public ComposedBinding(ComposedBindingSpec spec)
    {
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
        _factories = spec.Factories;

        if (spec.Calls.Count == 0)
            throw new PortBindingViolationException(
                spec.PortKey, $"端口 '{spec.PortKey}' 声明 composed 绑定但未声明任何调用（calls 为空）");

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var call in spec.Calls)
            if (!names.Add(call.As))
                throw new PortBindingViolationException(
                    spec.PortKey, $"端口 '{spec.PortKey}' 的调用引用名 '{call.As}' 重复（投影引用将歧义）");

        _calls = spec.Calls.Select(Resolve).ToArray();
    }

    public ValueTask<object?> InvokeAsync(IPortInput input, CancellationToken ct = default)
        => ValueTask.FromResult(Invoke(input));

    private object? Invoke(IPortInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var deps = BuildDeps(input);

        var results = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var call in _calls)
            results[call.Spec.As] = call.Invoke(input, deps);

        var product = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in _spec.Project)
            product[field.Field] = Project(field, deps, results);

        return product;
    }

    // ── 依赖构建（new / factory / fake）────────────────────────────────────────

    /// <summary>按声明序构建依赖（下标引用只能指向更早的依赖：保证构建拓扑无环）。</summary>
    /// <remarks>构建语义单源 = <see cref="ComposedDeps"/>（与脚本绑定 <c>deps</c> 共用）。</remarks>
    private object?[] BuildDeps(IPortInput input)
        => ComposedDeps.Build(_spec.Deps, input, _factories, _spec.PortKey);

    // 依赖构建（new / factory / fake）语义单源：ComposedDeps（组合绑定与脚本绑定共用）

    // 替身装配 / 依赖引用 / 构造选择语义单源：ComposedDeps（组合绑定与脚本绑定共用）

    /// <summary>类型解析（含开放泛型闭合：<c>GenericArgs</c> 按序闭合泛型形参）。</summary>
    /// <remarks>解析语义单源 = <see cref="ComposedDeps.ResolveType"/>。</remarks>
    internal static Type ResolveType(string typeName, IReadOnlyList<string>? genericArgs)
        => ComposedDeps.ResolveType(typeName, genericArgs);

    // ── 调用解析（静态 / 实例；方法 / 成员读取）──────────────────────────────

    private ResolvedCall Resolve(ComposedCallSpec call)
    {
        if (string.IsNullOrWhiteSpace(call.Method))
            throw new PortBindingViolationException(
                _spec.PortKey, $"端口 '{_spec.PortKey}' 的调用 '{call.As}' 缺少 method");

        var type = ResolveCallType(call, out var depIndex);
        var isStatic = depIndex is null;
        var expect = call.Expect is { Length: > 0 } expectName
            ? ReflectivePortResolver.ResolveType(expectName)
            : null;

        if (call.IsProperty)
            return new ResolvedCall(
                _spec.PortKey, call, depIndex, expect,
                member: ReflectivePortResolver.ResolveMember(type, _spec.PortKey, call.Method, isStatic));

        return new ResolvedCall(
            _spec.PortKey, call, depIndex, expect,
            method: ReflectivePortResolver.ResolveMethod(
                type, _spec.PortKey, call.Method, call.Args?.Count ?? 0, isStatic));
    }

    /// <summary>调用所属类型：静态调用取 <c>target</c>；实例调用取依赖的 <c>contract</c> / <c>new</c> 类型。</summary>
    private Type ResolveCallType(ComposedCallSpec call, out int? depIndex)
    {
        depIndex = call.Dep;
        if (call.Dep is not { } index)
        {
            if (string.IsNullOrWhiteSpace(call.Target))
                throw new PortBindingViolationException(
                    _spec.PortKey, $"端口 '{_spec.PortKey}' 的调用 '{call.As}' 缺少 target（静态调用）或 dep（实例调用）");

            return ReflectivePortResolver.ResolveType(call.Target!);
        }

        if (index < 0 || index >= _spec.Deps.Count)
            throw new PortBindingViolationException(
                _spec.PortKey,
                $"端口 '{_spec.PortKey}' 的调用 '{call.As}' 引用了不存在的依赖下标 {index}（依赖 {_spec.Deps.Count} 个）");

        var dep = _spec.Deps[index];
        var typeName = dep.Contract ?? dep.New;
        if (typeName is not { Length: > 0 })
            throw new PortBindingViolationException(
                _spec.PortKey,
                $"端口 '{_spec.PortKey}' 的调用 '{call.As}' 对 dep[{index}] 取实例成员，但该依赖未声明 contract / new" +
                "（实例类型不可知）");

        return ResolveType(typeName, dep.GenericArgs);
    }

    // ── 投影 ────────────────────────────────────────────────────────────────

    /// <summary>按取值法投影（机制枚举，不含领域谓词；未知取值法 fail-fast）。</summary>
    private object? Project(ComposedFieldSpec field, object?[] deps, IReadOnlyDictionary<string, object?> results)
    {
        switch (field.Select)
        {
            case "value":
                return Result(field, results);

            case "count":
                return Count(Result(field, results), field.Field);

            case "notNull":
                return Result(field, results) is not null;

            case "text":
                return Result(field, results)?.ToString();

            case "message":
                return (Result(field, results) as Exception)?.Message;

            case "thrown":
                return Result(field, results) is Exception;

            case "countEqual":
                if (field.Other is not { Length: > 0 } other || !results.TryGetValue(other, out var right))
                    throw new PortBindingViolationException(
                        _spec.PortKey,
                        $"端口 '{_spec.PortKey}' 的 countEqual 字段 '{field.Field}' 缺少可解析的比较调用（other='{field.Other}'）");

                return Count(Result(field, results), field.Field) == Count(right, field.Field);

            case "path":
                if (field.Path is not { Length: > 0 } path)
                    throw new PortBindingViolationException(
                        _spec.PortKey, $"端口 '{_spec.PortKey}' 的 path 字段 '{field.Field}' 缺少 path（属性路径）");

                return PathOf(Result(field, results), path);

            case "pathText":
                if (field.Path is not { Length: > 0 } textPath)
                    throw new PortBindingViolationException(
                        _spec.PortKey, $"端口 '{_spec.PortKey}' 的 pathText 字段 '{field.Field}' 缺少 path（属性路径）");

                return PathOf(Result(field, results), textPath)?.ToString();

            case "fakeCount":
                if (field.Dep is not { } index || index < 0 || index >= deps.Length || deps[index] is not { } fake)
                    throw new PortBindingViolationException(
                        _spec.PortKey,
                        $"端口 '{_spec.PortKey}' 的 fakeCount 字段 '{field.Field}' 缺少可解析的依赖（dep={field.Dep}）");

                return ProxyFakes.CountOf(fake, field.Method ?? throw Missing(field, "method"), field.Key);

            default:
                throw new PortBindingViolationException(
                    _spec.PortKey,
                    $"端口 '{_spec.PortKey}' 的投影字段 '{field.Field}' 取值法 '{field.Select}' 未知" +
                    "（value|count|notNull|text|message|thrown|countEqual|path|pathText|fakeCount）");
        }
    }

    /// <summary>取某次调用结果（引用必须命中已声明调用）。</summary>
    private object? Result(ComposedFieldSpec field, IReadOnlyDictionary<string, object?> results)
    {
        if (field.Call is not { Length: > 0 } call || !results.TryGetValue(call, out var value))
            throw new PortBindingViolationException(
                _spec.PortKey, $"端口 '{_spec.PortKey}' 的投影字段 '{field.Field}' 引用了未声明的调用 '{field.Call}'");

        return value;
    }

    private PortBindingViolationException Missing(ComposedFieldSpec field, string key)
        => new(_spec.PortKey, $"端口 '{_spec.PortKey}' 的字段 '{field.Field}' 取值法 '{field.Select}' 缺少 {key}");

    /// <summary>属性路径取值（<c>A.B.C</c>：逐段属性优先、回落字段、忽略大小写；任一段缺失 → null）。</summary>
    private static object? PathOf(object? value, string path) => PortValues.PathOf(value, path);

    /// <summary>计数（集合 / 数组 / 字符串长度；未知载体 fail-fast，防静默投影为 0）。</summary>
    private int Count(object? value, string field)
        => PortValues.TryCount(value)
           ?? throw new PortBindingViolationException(
               _spec.PortKey,
               $"端口 '{_spec.PortKey}' 的投影字段 '{field}' 取值法 count 不适用于 {value!.GetType().Name}");

    /// <summary>解析后的调用：构造实参 → 执行（静态 / 实例）→ 异常归一（期望异常转结果）→ 异步折取。</summary>
    private sealed class ResolvedCall
    {
        private readonly string _portKey;
        private readonly Type? _expect;
        private readonly MethodInfo? _method;
        private readonly MemberInfo? _member;

        internal ResolvedCall(
            string portKey, ComposedCallSpec spec, int? depIndex, Type? expect,
            MethodInfo? method = null, MemberInfo? member = null)
        {
            _portKey = portKey;
            Spec = spec;
            DepIndex = depIndex;
            _expect = expect;
            _method = method;
            _member = member;
        }

        internal ComposedCallSpec Spec { get; }

        /// <summary>实例调用的依赖下标（null = 静态调用）。</summary>
        internal int? DepIndex { get; }

        internal object? Invoke(IPortInput input, object?[] deps)
            => PortInvocation.Execute(() => Execute(input, deps), _expect);

        private object? Execute(IPortInput input, object?[] deps)
        {
            var instance = DepIndex is { } index ? deps[index] : null;

            if (_method is not null)
            {
                var args = ComposedArgs.Build(
                    _method.GetParameters(), Spec.Args, input, deps, _portKey, $"call '{Spec.As}'");
                return Fold(_method.Invoke(instance, args));
            }

            var value = _member switch
            {
                PropertyInfo property => property.GetValue(instance),
                FieldInfo field => field.GetValue(instance),
                _ => throw new PortBindingViolationException(
                    _portKey, $"端口 '{_portKey}' 的调用 '{Spec.As}' 未解析出成员（声明与解析不一致）"),
            };

            return Fold(value);
        }

        /// <summary>异步折取：<c>Task</c>/<c>Task&lt;T&gt;</c>/<c>ValueTask&lt;T&gt;</c> 同步取值（异常原样上抛）。</summary>
        private object? Fold(object? value) => PortInvocation.Fold(value);
    }
}
