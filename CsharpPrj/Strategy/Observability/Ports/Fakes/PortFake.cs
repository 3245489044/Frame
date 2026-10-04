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
/// 应答脚本项（G1）：按「成员名 / 调用序」给应答或抛指定异常（通用替身机制，服务任何接口桩）。
/// <para>本件只认不透明名与值（L1 红线：零领域词汇、无控制流、无表达式语言）。</para>
/// </summary>
public sealed record FakeCall
{
    /// <summary>目标成员名（方法名）；空 = 任意成员。</summary>
    public string? Member { get; init; }

    /// <summary>调用序（该成员第 N 次调用，0 起）；空 = 该成员每次调用都适用。</summary>
    public int? Position { get; init; }

    /// <summary>应答值（<see cref="HasValue"/> 区分「应答 null」与「未给值」）。</summary>
    public object? Value { get; init; }

    /// <summary>应答已给值（<c>Value = null</c> 亦为给值）。</summary>
    public bool HasValue { get; init; }

    /// <summary>抛出异常类型全名（与 <see cref="Value"/> 二选一；类型须可解析，否则即抛）。</summary>
    public string? ThrowType { get; init; }

    /// <summary>抛出异常消息（可空；类型无 (string) 构造时回落无参构造）。</summary>
    public string? Message { get; init; }
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

    /// <summary>
    /// 应答脚本（G1；空 = 不启用）。命中脚本项即给值 / 抛异常，优先于 <see cref="Values"/> 与 <see cref="Inner"/>；
    /// 未命中则回落常规形态（序列 / 转发 / 返回类型默认值）。
    /// </summary>
    public IReadOnlyList<FakeCall>? Calls { get; init; }

    /// <summary>
    /// 调用捕获成员（G8；空 = 不捕获）。非空时每次该成员被调用，其**实参数组**按调用序入捕获表，
    /// 供投影取值法 <c>capturedCount</c> / <c>capturedFirst</c> / <c>capturedLast</c> 读取。
    /// </summary>
    public string? CaptureMember { get; init; }
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
        PortGuard.Required(options, nameof(options));

        if (options.Calls is { Count: > 0 })
            throw new InvalidOperationException(
                "[port:fake] calls（按成员应答脚本）只适用于代理载体——委托载体无成员名可分派");

        if (options.CaptureMember is not null)
            throw new InvalidOperationException(
                "[port:fake] capture（按成员调用捕获）只适用于代理载体——委托载体无成员名可分派");

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
        PortGuard.Required(contract, nameof(contract));
        PortGuard.Required(options, nameof(options));

        return kind switch
        {
            FakeKind.Recording => Build(contract, options.Inner, options.Methods, counters: null, fixedValues: null, notExpected: false, reuseLast: false, calls: options.Calls, captureMember: options.CaptureMember),
            FakeKind.Counting or FakeKind.Proxy => Build(contract, options.Inner, options.Methods, new CallCounter(), fixedValues: null, notExpected: false, reuseLast: false, calls: options.Calls, captureMember: options.CaptureMember),
            FakeKind.Fixed => Build(contract, options.Inner, options.Methods, counters: null, fixedValues: new[] { options.HasValue ? options.Value : null }, notExpected: false, reuseLast: true, calls: options.Calls, captureMember: options.CaptureMember),
            FakeKind.Sequence => Build(contract, options.Inner, options.Methods, counters: null, fixedValues: Values(options), notExpected: false, reuseLast: false, calls: options.Calls, captureMember: options.CaptureMember),
            FakeKind.NotExpected => Build(contract, options.Inner, options.Methods, counters: null, fixedValues: null, notExpected: true, reuseLast: false, calls: options.Calls, captureMember: options.CaptureMember),
            _ => throw new InvalidOperationException(
                $"[port:fake] 代理载体不支持形态 '{kind}'（可用：recording|counting|proxy|fixed|sequence|notExpected）"),
        };
    }

    /// <summary>取替身调用计数（投影 <c>fakeCount</c> 入口）；非计数替身即抛（防静默取 0）。</summary>
    public static int CountOf(object fake, string method, string? key = null)
    {
        PortGuard.Required(fake, nameof(fake));
        PortGuard.Required(method, nameof(method));

        return fake is FakeDispatchProxy { Counter: { } counter }
            ? counter.Count(method, key)
            : throw new InvalidOperationException(
                $"[port:fake] 目标不是本件生成的计数替身（方法 '{method}' 的调用计数不可用）");
    }

    /// <summary>
    /// 取替身捕获的调用实参序（投影 <c>capturedCount</c> / <c>capturedFirst</c> / <c>capturedLast</c> 入口）；
    /// 非捕获替身即抛（防静默取空序）。<paramref name="member"/> 为空 = 主捕获成员。
    /// </summary>
    public static IReadOnlyList<object?[]> CapturedOf(object fake, string? member = null)
    {
        PortGuard.Required(fake, nameof(fake));

        return fake is FakeDispatchProxy { Capture: { } capture }
            ? capture.Of(member)
            : throw new InvalidOperationException(
                $"[port:fake] 目标不是本件生成的捕获替身（成员 '{member ?? "(default)"}' 的调用实参不可用）");
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
            PortGuard.Required(input, nameof(input));
            return ValueTask.FromResult(_invoke(input));
        }
    }

    /// <summary>统一装配：生成契约代理并按形态绑定行为（脚本 / 转发 / 计数 / 捕获 / 固定应答 / 序列 / 负向守卫）。</summary>
    private static object Build(
        Type contract, object? inner, IReadOnlyList<string>? methods, CallCounter? counters,
        IReadOnlyList<object?>? fixedValues, bool notExpected, bool reuseLast,
        IReadOnlyList<FakeCall>? calls = null, string? captureMember = null)
    {
        PortGuard.Required(contract, nameof(contract));
        if (!contract.IsInterface)
            throw new InvalidOperationException(
                $"[port:fake] 契约 '{contract.FullName}' 不是接口（代理替身只支持接口形态）");

        var proxy = (FakeDispatchProxy)DispatchProxy.Create(contract, typeof(FakeDispatchProxy));
        var watched = methods is { Count: > 0 } ? new HashSet<string>(methods, StringComparer.Ordinal) : null;
        var script = calls is { Count: > 0 } ? new FakeCallScript(calls) : null;
        var captures = captureMember is { } ? new FakeCaptureTable() : null;
        var index = 0;

        proxy.Initialize((method, args) =>
        {
            var isWatched = watched is null || watched.Contains(method.Name);

            if (captures is not null && IsCaptured(captureMember!, method.Name))
                captures.Add(method.Name, args);

            if (notExpected && isWatched)
                throw new PortCallNotExpectedException($"{contract.Name}.{method.Name}");

            if (counters is not null && isWatched)
                counters.Add(method.Name, KeyOf(args));

            if (script is not null && script.TryResolve(method.Name, out var call))
            {
                if (call.ThrowType is { Length: > 0 } throwType)
                    throw ForgeThrow(throwType, call.Message, contract, method);

                return Adapt(method.ReturnType, call.HasValue ? call.Value : null);
            }

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
        }, counters, captures);

        return proxy;
    }

    /// <summary>本次调用是否入捕获表（G8：声明成员为空 = 捕获全部成员）。</summary>
    private static bool IsCaptured(string member, string called)
        => member.Length == 0 || string.Equals(member, called, StringComparison.Ordinal);

    /// <summary>主键：首个字符串实参（无 → 空串）。</summary>
    private static string KeyOf(object?[] args)
        => args.FirstOrDefault(argument => argument is string) as string ?? string.Empty;

    /// <summary>
    /// 转发到真实实现：先按「方法名 + 形参类型」解析公共方法，未命中再经**接口映射**解析**显式接口实现**
    /// （G2：显式实现不落在公共面上，<see cref="Type.GetMethod(string, Type[])"/> 找不到 ⇒ 反射按名匹配不足）。
    /// 仍不可解析即抛，防静默空转。
    /// </summary>
    private static object? Forward(object inner, MethodInfo method, object?[] args)
    {
        var target = ResolveForwardTarget(inner.GetType(), method);

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

    /// <summary>转发目标解析（公共方法优先，回落显式接口实现的接口映射）。</summary>
    private static MethodInfo? ResolveForwardTarget(Type implementation, MethodInfo method)
    {
        var parameters = method.GetParameters().Select(parameter => parameter.ParameterType).ToArray();

        try
        {
            if (implementation.GetMethod(method.Name, parameters) is { } direct) return direct;
        }
        catch (AmbiguousMatchException)
        {
            // 同名同签名多候选（泛型元数差异）：交由接口映射按槽位精确解析。
        }

        var declared = method.DeclaringType;
        if (declared is null || !declared.IsInterface || !declared.IsAssignableFrom(implementation)) return null;

        var map = implementation.GetInterfaceMap(declared);

        for (var i = 0; i < map.InterfaceMethods.Length; i++)
        {
            if (!string.Equals(map.InterfaceMethods[i].Name, method.Name, StringComparison.Ordinal)) continue;
            if (!map.InterfaceMethods[i].GetParameters().Select(p => p.ParameterType).SequenceEqual(parameters)) continue;

            return map.TargetMethods[i];
        }

        return null;
    }

    /// <summary>按声明类型名抛出异常（类型须可解析且为异常类型；构造不可用即抛，禁静默回落）。</summary>
    private static Exception ForgeThrow(string typeName, string? message, Type contract, MethodInfo method)
        => PortInvocation.ForgeThrow(typeName, message, $"{contract.Name}.{method.Name}");

    /// <summary>
    /// 应答值适配（D14 单源）：载荷按内层类型归一（<see cref="Coerce"/>）后，交
    /// <see cref="PortInvocation.Realize"/> 统一包装为已完成任务 —— 与端口代理 / 委托体同一口径；
    /// 非任务返回类型原样走 <see cref="Coerce"/>。
    /// </summary>
    private static object? Adapt(Type returnType, object? value)
    {
        if (PortInvocation.TryPayloadType(returnType, out var payloadType))
            return PortInvocation.Realize(Coerce(value, payloadType), returnType);

        return returnType == typeof(Task) || returnType == typeof(ValueTask)
            ? PortInvocation.Realize(null, returnType)
            : Coerce(value, returnType);
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

/// <summary>
/// 替身应答脚本解析（G1）：按「成员名 + 调用序」定位应答项（表驱动；不含控制流与表达式语言）。
/// <para>
/// 优先级（声明序内取最低秩，同秩取先声明者）：① 成员 + 精确序 → ② 成员 + 任意序 →
/// ③ 任意成员 + 全局序 → ④ 任意成员 + 任意序；未命中即返回 false（回落常规形态）。
/// </para>
/// </summary>
internal sealed class FakeCallScript
{
    private readonly IReadOnlyList<FakeCall> _calls;
    private readonly Dictionary<string, int> _memberPositions = new(StringComparer.Ordinal);
    private readonly object _gate = new();
    private int _globalPosition;

    internal FakeCallScript(IReadOnlyList<FakeCall> calls) => _calls = calls;

    /// <summary>解析本次调用的应答项（无匹配返回 false）。</summary>
    internal bool TryResolve(string member, out FakeCall call)
    {
        int memberPosition;
        int globalPosition;

        lock (_gate)
        {
            memberPosition = _memberPositions.TryGetValue(member, out var seen) ? seen : 0;
            _memberPositions[member] = memberPosition + 1;
            globalPosition = _globalPosition++;
        }

        FakeCall? best = null;
        var bestRank = int.MaxValue;

        foreach (var candidate in _calls)
        {
            var rank = Rank(candidate, member, memberPosition, globalPosition);
            if (rank >= bestRank) continue;

            best = candidate;
            bestRank = rank;
        }

        call = best!;
        return best is not null;
    }

    /// <summary>匹配秩（越小越优先；<see cref="int.MaxValue"/> = 不匹配）。</summary>
    private static int Rank(FakeCall candidate, string member, int memberPosition, int globalPosition)
    {
        var named = candidate.Member is { Length: > 0 };

        if (named && !string.Equals(candidate.Member, member, StringComparison.Ordinal)) return int.MaxValue;

        if (named)
            return candidate.Position == memberPosition ? 0
                : candidate.Position is null ? 1
                : int.MaxValue;

        return candidate.Position == globalPosition ? 2
            : candidate.Position is null ? 3
            : int.MaxValue;
    }
}

/// <summary>
/// 调用捕获表（G8）：按成员分桶记录**调用实参数组**（声明序 = 调用序）；并发安全（替身可被并行调用）。
/// <para>分工：本件只记录不判定（断言语义由投影取值法承担，L1 红线：零领域词汇）。</para>
/// </summary>
internal sealed class FakeCaptureTable
{
    private readonly Dictionary<string, List<object?[]>> _byMember = new(StringComparer.Ordinal);
    private readonly List<object?[]> _all = [];
    private readonly object _gate = new();

    /// <summary>记录一次调用的实参（入表前快照：调用方不复用数组）。</summary>
    internal void Add(string member, object?[] args)
    {
        var snapshot = args.ToArray();

        lock (_gate)
        {
            _all.Add(snapshot);

            if (!_byMember.TryGetValue(member, out var bucket)) _byMember[member] = bucket = [];
            bucket.Add(snapshot);
        }
    }

    /// <summary>取某成员的调用实参序（成员为空 = 全部成员的调用序）；未记录成员 → 空序。</summary>
    internal IReadOnlyList<object?[]> Of(string? member)
    {
        lock (_gate)
        {
            if (member is not { Length: > 0 }) return _all.ToArray();

            return _byMember.TryGetValue(member, out var bucket) ? bucket.ToArray() : [];
        }
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

    /// <summary>调用捕获表（仅捕获形态非空；供 <see cref="PortFake.CapturedOf"/> 读取）。</summary>
    internal FakeCaptureTable? Capture { get; private set; }

    internal void Initialize(
        Func<MethodInfo, object?[], object?> handler, CallCounter? counter, FakeCaptureTable? capture = null)
    {
        _handler = handler;
        Counter = counter;
        Capture = capture;
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
        PortGuard.Required(respond, nameof(respond));
        return PortFake.Create(FakeKind.Recording, new FakeOptions { Respond = respond });
    }

    /// <summary>固定应答替身（值 / 结构）。</summary>
    public static IPortBinding Fixed(object? value)
        => PortFake.Create(FakeKind.Fixed, new FakeOptions { HasValue = true, Value = value });

    /// <summary>固定应答替身（按输入计算固定结构，如多键 seed 表）。</summary>
    public static IPortBinding FixedBy(Func<IPortInput, object?> projector)
    {
        PortGuard.Required(projector, nameof(projector));
        return PortFake.Create(FakeKind.Fixed, new FakeOptions { Respond = projector });
    }

    /// <summary>回显替身：第一个位置实参经投影后即应答（输入决定输出）。</summary>
    public static IPortBinding Echo(Func<object?, object?> projector)
    {
        PortGuard.Required(projector, nameof(projector));
        return PortFake.Create(FakeKind.Echo, new FakeOptions { Projector = projector });
    }

    /// <summary>回显替身（按完整输入投影）。</summary>
    public static IPortBinding EchoBy(Func<IPortInput, object?> projector)
    {
        PortGuard.Required(projector, nameof(projector));
        return PortFake.Create(FakeKind.Recording, new FakeOptions { Respond = projector });
    }

    /// <summary>负向守卫替身：被调用即失败（用例声明该端口不应被触达）。</summary>
    public static IPortBinding NotExpected() => PortFake.Create(FakeKind.NotExpected, new FakeOptions());

    /// <summary>按序应答替身：按调用序吐出应答，耗尽即失败（驱动多轮 / 重试）。</summary>
    public static IPortBinding Sequence(params object?[] values) => SequenceOf(values);

    /// <summary>按序应答替身（序列来源可枚举）。</summary>
    public static IPortBinding SequenceOf(IEnumerable<object?> values)
    {
        PortGuard.Required(values, nameof(values));
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
