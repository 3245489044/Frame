namespace Strategy.Observability.Ports;

using System.Reflection;
using System.Text.Json;
using Strategy.Observability.Diagnostics;

/// <summary>
/// 替身形态（R4 唯一枚举）：委托载体（<see cref="PortFake.Create"/>）与代理载体（<see cref="PortFake.CreateProxy"/>）共用同一形态词，
/// 消除迁移前「委托 5 形态 / 代理 5 形态」两套枚举的人工对账（D-3）。
/// </summary>
public enum FakeKind
{
    /// <summary>记录型：仅观察调用序与实参（应答为 null；调用记录由端口目录承担），代理载体可包装真实实现。</summary>
    Recording,

    /// <summary>固定应答：所有调用返回同一值（<see cref="FakeOptions.Value"/>）。</summary>
    Fixed,

    /// <summary>回显：首个位置实参经投影即应答（仅委托载体；<see cref="FakeOptions.Projector"/>）。</summary>
    Echo,

    /// <summary>序列应答：按调用序取值，用尽即抛（<see cref="FakeOptions.Values"/>）。</summary>
    Sequence,

    /// <summary>负向守卫：关注方法被调用即抛（代理载体可指定未关注方法转发目标）。</summary>
    NotExpected,

    /// <summary>计数装饰（仅代理载体）：转发到真实实现并按「方法名 + 主键」计次。</summary>
    Counting,

    /// <summary>代理载体的通用转发入口（与 <see cref="Counting"/> 同义；保留双词表以兼容台账 <c>deps.fake</c> 用语）。</summary>
    Proxy,
}

/// <summary>
/// 替身选项（R4 联合形态）：两载体共用一个选项对象，缺项由 <see cref="PortFake"/> 按形态显式校验
/// （缺契约 / 缺序列即抛，不静默造空）。
/// </summary>
public sealed record FakeOptions
{
    /// <summary>自定义应答（委托载体的 recording / fixedBy / echoBy 用；由输入决定应答）。</summary>
    public Func<IPortInput, object?>? Respond { get; init; }

    /// <summary>首实参投影（委托载体的 echo 用）。</summary>
    public Func<object?, object?>? Projector { get; init; }

    /// <summary>固定应答值（<see cref="HasValue"/> 区分「固定 null」与「未给」）。</summary>
    public object? Value { get; init; }

    /// <summary>固定应答已给值（<c>Fixed(null)</c> 亦为给值）。</summary>
    public bool HasValue { get; init; }

    /// <summary>序列应答值（用尽即抛；<see cref="FakeKind.Fixed"/> 复用首项）。</summary>
    public IReadOnlyList<object?>? Values { get; init; }

    /// <summary>代理载体的转发目标（真实实现；null = 不转发，按返回类型给默认值）。</summary>
    public object? Inner { get; init; }

    /// <summary>代理载体的关注方法名（空 = 全部方法）。</summary>
    public IReadOnlyList<string>? Methods { get; init; }
}

/// <summary>
/// 唯一替身面（R4 / 路线 §2.5）：合并迁移前 <c>StandardFakes</c>（委托载体）与 <c>ProxyFakes</c>（代理载体）
/// —— **同一形态枚举、同一异常族、同一序列语义**；两载体的适用于 <see cref="Create"/> / <see cref="CreateProxy"/>
/// 按 <see cref="FakeKind"/> 显式校验（形态不适用即抛，不静默降级）。
/// <para>
/// 分工：本件不承担调用记录（记录由端口目录的记录面统一承担，每次调用恰一条）；计数仅由代理载体的
/// <see cref="CountOf"/> 供给投影取值，不承载断言语义（L1 红线：零领域词汇）。
/// </para>
/// </summary>
public static class PortFake
{
    /// <summary>委托载体：按形态构造 <see cref="IPortBinding"/> 替身。</summary>
    public static IPortBinding Create(FakeKind kind, FakeOptions options)
    {
        ArgumentNullException.ThrowIfNull(options);

        return kind switch
        {
            FakeKind.Recording => new DelegateBinding(options.Respond ?? (_ => null)),
            FakeKind.Fixed => options.Respond is { } projector
                ? new DelegateBinding(projector)
                : new DelegateBinding(_ => options.HasValue ? options.Value : null),
            FakeKind.Echo => EchoBinding(options),
            FakeKind.Sequence => SequenceBinding(options),
            FakeKind.NotExpected => new DelegateBinding(input => throw new PortCallNotExpectedException(input.PortKey)),
            _ => throw new InvalidOperationException(
                $"[port:fake] 委托载体不支持形态 '{kind}'（可用：recording|fixed|echo|sequence|notExpected）"),
        };
    }

    /// <summary>代理载体：按<b>契约接口</b>生成替身（转发 / 计数 / 固定应答 / 序列 / 负向守卫）。</summary>
    public static object CreateProxy(Type contract, FakeKind kind, FakeOptions options)
    {
        ArgumentNullException.ThrowIfNull(contract);
        ArgumentNullException.ThrowIfNull(options);

        return kind switch
        {
            FakeKind.Recording => Build(contract, options.Inner, options.Methods, counters: null, fixedValues: null, notExpected: false, reuseLast: false),
            FakeKind.Counting or FakeKind.Proxy => Build(contract, options.Inner, options.Methods, new CallCounter(), fixedValues: null, notExpected: false, reuseLast: false),
            FakeKind.Fixed => Build(contract, options.Inner, options.Methods, counters: null, fixedValues: new[] { options.HasValue ? options.Value : null }, notExpected: false, reuseLast: true),
            FakeKind.Sequence => Build(contract, options.Inner, options.Methods, counters: null, fixedValues: Values(options), notExpected: false, reuseLast: false),
            FakeKind.NotExpected => Build(contract, options.Inner, options.Methods, counters: null, fixedValues: null, notExpected: true, reuseLast: false),
            _ => throw new InvalidOperationException(
                $"[port:fake] 代理载体不支持形态 '{kind}'（可用：recording|counting|proxy|fixed|sequence|notExpected）"),
        };
    }

    /// <summary>取替身调用计数（投影 <c>fakeCount</c> 入口）；非计数替身即抛（防静默取 0）。</summary>
    public static int CountOf(object fake, string method, string? key = null)
    {
        ArgumentNullException.ThrowIfNull(fake);
        ArgumentNullException.ThrowIfNull(method);

        return fake is FakeDispatchProxy { Counter: { } counter }
            ? counter.Count(method, key)
            : throw new InvalidOperationException(
                $"[port:fake] 目标不是本件生成的计数替身（方法 '{method}' 的调用计数不可用）");
    }

    /// <summary>回显（首实参经投影；缺投影即抛）。</summary>
    private static IPortBinding EchoBinding(FakeOptions options)
    {
        var projector = options.Projector ?? throw new InvalidOperationException(
            "[port:fake] echo 形态缺 Projector（首实参投影）");

        return new DelegateBinding(input => projector(input.Args.Count > 0 ? input.Args[0] : null));
    }

    /// <summary>委托载体的序列应答（用尽即抛；耗尽位置沿用迁移前口径 <c>0</c>）。</summary>
    private static IPortBinding SequenceBinding(FakeOptions options)
    {
        var queue = new Queue<object?>(Values(options));
        return new DelegateBinding(input =>
        {
            if (queue.Count == 0) throw new FakeSequenceExhaustedException(input.PortKey, 0);
            return queue.Dequeue();
        });
    }

    /// <summary>序列值（缺项即抛：声明了序列却不给值属声明残缺）。</summary>
    private static IReadOnlyList<object?> Values(FakeOptions options)
        => options.Values ?? throw new InvalidOperationException("[port:fake] sequence 形态缺 Values（序列应答值）");

    /// <summary>把委托收敛为绑定（委托载体的唯一实现，避免 5 个平行私有类型）。</summary>
    private sealed class DelegateBinding : IPortBinding
    {
        private readonly Func<IPortInput, object?> _invoke;

        internal DelegateBinding(Func<IPortInput, object?> invoke) => _invoke = invoke;

        public ValueTask<object?> InvokeAsync(IPortInput input, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            return ValueTask.FromResult(_invoke(input));
        }
    }

    /// <summary>统一装配：生成契约代理并按形态绑定行为（转发 / 计数 / 固定应答 / 序列 / 负向守卫）。</summary>
    private static object Build(
        Type contract, object? inner, IReadOnlyList<string>? methods, CallCounter? counters,
        IReadOnlyList<object?>? fixedValues, bool notExpected, bool reuseLast)
    {
        ArgumentNullException.ThrowIfNull(contract);
        if (!contract.IsInterface)
            throw new InvalidOperationException(
                $"[port:fake] 契约 '{contract.FullName}' 不是接口（代理替身只支持接口形态）");

        var proxy = (FakeDispatchProxy)DispatchProxy.Create(contract, typeof(FakeDispatchProxy));
        var watched = methods is { Count: > 0 } ? new HashSet<string>(methods, StringComparer.Ordinal) : null;
        var index = 0;

        proxy.Initialize((method, args) =>
        {
            var isWatched = watched is null || watched.Contains(method.Name);

            if (notExpected && isWatched)
                throw new PortCallNotExpectedException($"{contract.Name}.{method.Name}");

            if (counters is not null && isWatched)
                counters.Add(method.Name, KeyOf(args));

            if (fixedValues is { Count: > 0 })
            {
                // 序列口径单源：多值用尽即抛；单值（fixed）复用首项。
                var position = Interlocked.Increment(ref index) - 1;
                if (position >= fixedValues.Count && !reuseLast)
                    throw new FakeSequenceExhaustedException($"{contract.Name}.{method.Name}", position);

                return Adapt(method.ReturnType, fixedValues[Math.Min(position, fixedValues.Count - 1)]);
            }

            return inner is null
                ? Adapt(method.ReturnType, null)
                : Forward(inner, method, args);
        }, counters);

        return proxy;
    }

    /// <summary>主键：首个字符串实参（无 → 空串）。</summary>
    private static string KeyOf(object?[] args)
        => args.FirstOrDefault(argument => argument is string) as string ?? string.Empty;

    /// <summary>转发到真实实现（按方法名 + 形参类型精确匹配；不可解析即抛，防静默空转）。</summary>
    private static object? Forward(object inner, MethodInfo method, object?[] args)
    {
        var target = inner.GetType().GetMethod(
            method.Name, method.GetParameters().Select(p => p.ParameterType).ToArray());

        if (target is null)
            throw new InvalidOperationException(
                $"[port:fake] 真实实现 {inner.GetType().Name} 上找不到可转发的方法 '{method.Name}'");

        try
        {
            return target.Invoke(inner, args);
        }
        catch (TargetInvocationException ex) when (ex.InnerException is { } innerException)
        {
            return PortInvocation.ExpectAny(innerException, expect: null);   // 原样上抛（保留原始栈）
        }
    }

    /// <summary>应答值适配：<c>Task</c>/<c>Task&lt;T&gt;</c>/<c>ValueTask</c> 包装为已完成任务；其余按返回类型反序列化。</summary>
    private static object? Adapt(Type returnType, object? value)
    {
        if (returnType == typeof(Task)) return Task.CompletedTask;
        if (returnType == typeof(ValueTask)) return ValueTask.CompletedTask;

        if (returnType.IsGenericType)
        {
            var definition = returnType.GetGenericTypeDefinition();
            var resultType = returnType.GetGenericArguments()[0];

            if (definition == typeof(Task<>))
                return typeof(Task)
                    .GetMethod(nameof(Task.FromResult))!
                    .MakeGenericMethod(resultType)
                    .Invoke(null, new[] { Coerce(value, resultType) });

            if (definition == typeof(ValueTask<>))
                return Activator.CreateInstance(returnType, Coerce(value, resultType));
        }

        return Coerce(value, returnType);
    }

    /// <summary>值 → 目标类型（<see cref="JsonElement"/> 按原始文本反序列化；其余直传；null 保留）。</summary>
    private static object? Coerce(object? value, Type target)
    {
        if (value is not JsonElement element) return value;

        if (element.ValueKind is JsonValueKind.Null or JsonValueKind.Undefined) return null;

        return target == typeof(JsonElement)
            ? element.Clone()
            : JsonSerializer.Deserialize(element.GetRawText(), target);
    }
}

/// <summary>调用计数表（方法名 + 主键 → 次数）；并发安全（替身可被并行调用）。</summary>
internal sealed class CallCounter
{
    private readonly Dictionary<(string Method, string Key), int> _counts = new();
    private readonly object _gate = new();

    /// <summary>计一次（主键为空串 = 无主键调用）。</summary>
    internal void Add(string method, string key)
    {
        lock (_gate)
        {
            var slot = (method, key);
            _counts[slot] = _counts.TryGetValue(slot, out var count) ? count + 1 : 1;
        }
    }

    /// <summary>取计数（<paramref name="key"/> 为空 = 该方法的全部主键之和）。</summary>
    internal int Count(string method, string? key)
    {
        lock (_gate)
        {
            if (key is not null)
                return _counts.TryGetValue((method, key), out var exact) ? exact : 0;

            var total = 0;
            foreach (var ((name, _), value) in _counts)
                if (string.Equals(name, method, StringComparison.Ordinal)) total += value;

            return total;
        }
    }
}

/// <summary>代理替身执行体（<see cref="DispatchProxy"/> 派生；行为由 <see cref="ProxyFakes"/> 装配）。</summary>
internal class FakeDispatchProxy : DispatchProxy
{
    private Func<MethodInfo, object?[], object?>? _handler;

    /// <summary>调用计数表（仅计数形态非空；供 <see cref="ProxyFakes.CountOf"/> 读取）。</summary>
    internal CallCounter? Counter { get; private set; }

    internal void Initialize(Func<MethodInfo, object?[], object?> handler, CallCounter? counter)
    {
        _handler = handler;
        Counter = counter;
    }

    protected override object? Invoke(MethodInfo? targetMethod, object?[]? args)
        => (_handler ?? throw new InvalidOperationException("[port:fake] 代理替身未装配行为"))
            (targetMethod!, args ?? Array.Empty<object?>());
}

/// <summary>
/// 标准替身（委托载体·<b>兼容壳</b>）：迁移前 5 原语的入口名与签名原样保留，实现全部转调 <see cref="PortFake"/>。
/// <para>保留理由：公开面被内核自证（<c>KernelRedLineTests.StandardFakes_SurfaceIsFivePrimitives</c>）与上层装配面消费，
/// 直接删除会造成编译破坏；退场时机 = R6（守卫切换为直接消费 <see cref="PortFake"/> 后）。</para>
/// </summary>
public static class StandardFakes
{
    /// <summary>记录型替身：无应答（null），仅用于观察调用序与实参（记录由端口目录承担）。</summary>
    public static IPortBinding Recording() => PortFake.Create(FakeKind.Recording, new FakeOptions());

    /// <summary>记录型替身 + 自定义应答（应答由输入决定）。</summary>
    public static IPortBinding Recording(Func<IPortInput, object?> respond)
    {
        ArgumentNullException.ThrowIfNull(respond);
        return PortFake.Create(FakeKind.Recording, new FakeOptions { Respond = respond });
    }

    /// <summary>固定应答替身（值 / 结构）。</summary>
    public static IPortBinding Fixed(object? value)
        => PortFake.Create(FakeKind.Fixed, new FakeOptions { HasValue = true, Value = value });

    /// <summary>固定应答替身（按输入计算固定结构，如多键 seed 表）。</summary>
    public static IPortBinding FixedBy(Func<IPortInput, object?> projector)
    {
        ArgumentNullException.ThrowIfNull(projector);
        return PortFake.Create(FakeKind.Fixed, new FakeOptions { Respond = projector });
    }

    /// <summary>回显替身：第一个位置实参经投影后即应答（输入决定输出）。</summary>
    public static IPortBinding Echo(Func<object?, object?> projector)
    {
        ArgumentNullException.ThrowIfNull(projector);
        return PortFake.Create(FakeKind.Echo, new FakeOptions { Projector = projector });
    }

    /// <summary>回显替身（按完整输入投影）。</summary>
    public static IPortBinding EchoBy(Func<IPortInput, object?> projector)
    {
        ArgumentNullException.ThrowIfNull(projector);
        return PortFake.Create(FakeKind.Recording, new FakeOptions { Respond = projector });
    }

    /// <summary>负向守卫替身：被调用即失败（用例声明该端口不应被触达）。</summary>
    public static IPortBinding NotExpected() => PortFake.Create(FakeKind.NotExpected, new FakeOptions());

    /// <summary>按序应答替身：按调用序吐出应答，耗尽即失败（驱动多轮 / 重试）。</summary>
    public static IPortBinding Sequence(params object?[] values) => SequenceOf(values);

    /// <summary>按序应答替身（序列来源可枚举）。</summary>
    public static IPortBinding SequenceOf(IEnumerable<object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        return PortFake.Create(FakeKind.Sequence, new FakeOptions { Values = values.ToArray() });
    }
}

/// <summary>
/// 通用代理替身（代理载体·<b>兼容壳</b>）：迁移前 6 入口的名称与签名原样保留，实现全部转调 <see cref="PortFake"/>。
/// <para>退场时机同 <see cref="StandardFakes"/>（R6）。</para>
/// </summary>
public static class ProxyFakes
{
    /// <summary>计数装饰：转发到 <paramref name="inner"/>，对关注方法按「方法名 + 主键」计次。</summary>
    public static object Counting(Type contract, object inner, IReadOnlyList<string>? methods = null)
        => PortFake.CreateProxy(contract, FakeKind.Counting, new FakeOptions { Inner = inner, Methods = methods });

    /// <summary>转发装饰：仅转发（记录由端口目录的调用记录面承担）。</summary>
    public static object Recording(Type contract, object inner, IReadOnlyList<string>? methods = null)
        => PortFake.CreateProxy(contract, FakeKind.Recording, new FakeOptions { Inner = inner, Methods = methods });

    /// <summary>固定应答：所有方法返回同一值（<c>Task</c>/<c>Task&lt;T&gt;</c> 返回类型自动包装为已完成任务）。</summary>
    public static object Fixed(Type contract, object? value)
        => PortFake.CreateProxy(contract, FakeKind.Fixed, new FakeOptions { HasValue = true, Value = value });

    /// <summary>序列应答：按调用次序取值，**用尽即抛**（口径与 <see cref="StandardFakes.Sequence"/> 同源）。</summary>
    public static object Sequence(Type contract, IReadOnlyList<object?> values)
        => PortFake.CreateProxy(contract, FakeKind.Sequence, new FakeOptions { Values = values });

    /// <summary>负向守卫：关注方法被调用即抛；未关注方法按 <paramref name="inner"/> 转发（null = 不转发，返回类型默认值）。</summary>
    public static object NotExpected(Type contract, IReadOnlyList<string>? methods = null, object? inner = null)
        => PortFake.CreateProxy(contract, FakeKind.NotExpected, new FakeOptions { Inner = inner, Methods = methods });

    /// <summary>取替身调用计数（<c>fakeCount</c> 投影入口）；非计数替身即抛（防静默取 0）。</summary>
    public static int CountOf(object fake, string method, string? key = null)
        => PortFake.CountOf(fake, method, key);
}
