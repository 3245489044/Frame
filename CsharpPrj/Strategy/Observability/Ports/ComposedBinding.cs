using System.Reflection;
using Strategy.Observability.Diagnostics;

namespace Strategy.Observability.Ports;

using DataCenter.Protocol.Standards;

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
    private readonly string _portKey;
    private readonly IReadOnlyList<DepStandard> _deps;
    private readonly IReadOnlyList<ComposedFieldStandard> _project;
    private readonly IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>> _factories;
    private readonly IReadOnlyList<ComposedCall> _calls;

    /// <summary>
    /// 按 ③ 绑定声明构造（测试 / 旧装配入口）：声明经 <see cref="LegacyStandard"/> 归一到 ① 标准声明（唯一数据源）
    /// 后与求值引擎共用同一套助手（单一实现点）。
    /// </summary>
    public ComposedBinding(ComposedBindingSpec spec)
        : this(
            (spec ?? throw new ArgumentNullException(nameof(spec))).PortKey,
            LegacyStandard.Impl(spec),
            spec.Factories)
    {
    }

    /// <summary>
    /// 按 ① 标准声明构造（求值面入口）：调用声明残缺、引用名重复、目标类型或成员不可解析 → 即抛（fail-fast，防静默绑错）。
    /// </summary>
    public ComposedBinding(
        string portKey, PortImplStandard impl,
        IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>>? factories = null)
    {
        ArgumentNullException.ThrowIfNull(impl);
        ArgumentException.ThrowIfNullOrWhiteSpace(portKey);

        _portKey = portKey;
        _deps = impl.Deps ?? Array.Empty<DepStandard>();
        _project = impl.Project ?? Array.Empty<ComposedFieldStandard>();
        _factories = factories ?? PortInjection.Empty.Factories;

        var calls = impl.Calls ?? Array.Empty<CallStandard>();
        if (calls.Count == 0)
            throw new PortBindingViolationException(
                portKey, $"端口 '{portKey}' 声明 composed 绑定但未声明任何调用（calls 为空）");

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var call in calls)
            if (!names.Add(call.As))
                throw new PortBindingViolationException(
                    portKey, $"端口 '{portKey}' 的调用引用名 '{call.As}' 重复（投影引用将歧义）");

        _calls = calls.Select(call => PortComposedCall.Resolve(_deps, portKey, call)).ToArray();
    }

    public ValueTask<object?> InvokeAsync(IPortInput input, CancellationToken ct = default)
        => ValueTask.FromResult(Invoke(input));

    /// <summary>
    /// 编排（R3-2 后为**薄适配**）：依赖构建（单源 = <see cref="ComposedDeps"/>）→ 逐调用执行
    /// （<see cref="PortComposedCall"/> / <see cref="ComposedCall"/>）→ 逐字段投影（<see cref="PortProjection"/>）。
    /// <para>调用与投影语义已迁入 <c>Eval\Ops\</c>（唯一实现点），本件只保留声明校验与执行编排。</para>
    /// </summary>
    private object? Invoke(IPortInput input)
    {
        ArgumentNullException.ThrowIfNull(input);

        var deps = ComposedDeps.Build(_deps, input, _factories, _portKey);

        var results = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var call in _calls)
            results[call.Spec.As] = call.Invoke(input, deps);

        var product = new Dictionary<string, object?>(StringComparer.Ordinal);
        foreach (var field in _project)
            product[field.Field] = PortProjection.Field(_portKey, field, deps, results);

        return product;
    }

    /// <summary>类型解析（含开放泛型闭合：<c>GenericArgs</c> 按序闭合泛型形参）；解析语义单源 = <see cref="ComposedDeps.ResolveType"/>。</summary>
    internal static Type ResolveType(string typeName, IReadOnlyList<string>? genericArgs)
        => ComposedDeps.ResolveType(typeName, genericArgs);
}
