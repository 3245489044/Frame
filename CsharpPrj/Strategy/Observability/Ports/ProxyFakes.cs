using System.Reflection;
using System.Text.Json;
using Strategy.Observability.Diagnostics;

namespace Strategy.Observability.Ports;

/// <summary>
/// 通用代理替身（E15 续 / 消除通道 C2）：按**契约接口**动态生成替身，把旧夹具里逐件手写的
/// 「计数装饰器 / 记录器 / 固定应答 / 序列应答 / 不该调用守卫」收敛为机制原语。
/// <para>
/// 零领域词汇：契约以 <see cref="Type"/> 给出，方法名与主键均为不透明字符串（L1 红线不破）；
/// 替身不承载断言语义——计数由 <see cref="CountOf"/> 供投影取值。
/// </para>
/// </summary>
public static class ProxyFakes
{
    /// <summary>计数装饰：转发到 <paramref name="inner"/>，对关注方法按「方法名 + 主键」计次（主键 = 首个字符串实参；无则空串）。</summary>
    public static object Counting(Type contract, object inner, IReadOnlyList<string>? methods = null)
        => Build(contract, inner, methods, new CallCounter(), fixedValues: null, notExpected: false, reuseLast: false);

    /// <summary>转发装饰：仅转发（记录由端口目录的调用记录面承担；留作对照与守卫样本）。</summary>
    public static object Recording(Type contract, object inner, IReadOnlyList<string>? methods = null)
        => Build(contract, inner, methods, counters: null, fixedValues: null, notExpected: false, reuseLast: false);

    /// <summary>固定应答：所有方法返回同一值（单值复用，<c>Task</c>/<c>Task&lt;T&gt;</c> 返回类型自动包装为已完成任务）。</summary>
    public static object Fixed(Type contract, object? value)
        => Build(contract, inner: null, methods: null, counters: null, fixedValues: new[] { value }, notExpected: false, reuseLast: true);

    /// <summary>序列应答：按调用次序取值，**用尽即抛**（口径与 <see cref="StandardFakes.Sequence"/> 同源）。</summary>
    public static object Sequence(Type contract, IReadOnlyList<object?> values)
        => Build(contract, inner: null, methods: null, counters: null, fixedValues: values, notExpected: false, reuseLast: false);

    /// <summary>负向守卫：关注方法被调用即抛；未关注方法按 <paramref name="inner"/> 转发（null = 不转发，返回类型默认值）。</summary>
    public static object NotExpected(Type contract, IReadOnlyList<string>? methods = null, object? inner = null)
        => Build(contract, inner, methods, counters: null, fixedValues: null, notExpected: true, reuseLast: false);

    /// <summary>取替身调用计数（<c>fakeCount</c> 投影入口）；非计数替身即抛（防静默取 0）。</summary>
    public static int CountOf(object fake, string method, string? key = null)
        => fake is FakeDispatchProxy { Counter: { } counter }
            ? counter.Count(method, key)
            : throw new InvalidOperationException(
                $"[port:fake] 目标不是本件生成的计数替身（方法 '{method}' 的调用计数不可用）");

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

