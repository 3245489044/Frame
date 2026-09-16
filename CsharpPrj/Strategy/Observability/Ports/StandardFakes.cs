using Strategy.Observability.Diagnostics;

namespace Strategy.Observability.Ports;

/// <summary>
/// 标准替身（E11）：把「测试侧私有替身」收敛为 5 个内核原语——
/// <c>recording</c> / <c>fixed</c> / <c>echo</c> / <c>notExpected</c> / <c>sequence</c>。
/// <para>
/// 调用记录不在此承担：由 <see cref="PortCatalog"/> 的 <see cref="CallLog"/> 统一记录（每次调用恰好一条），
/// 故替身可自由组合而不会重复计数。
/// </para>
/// </summary>
public static class StandardFakes
{
    /// <summary>记录型替身：无应答（null），仅用于观察调用序与实参（记录由端口目录承担）。</summary>
    public static IPortBinding Recording() => new DelegateBinding(_ => null);

    /// <summary>记录型替身 + 自定义应答（应答由输入决定）。</summary>
    public static IPortBinding Recording(Func<IPortInput, object?> respond)
    {
        ArgumentNullException.ThrowIfNull(respond);
        return new DelegateBinding(respond);
    }

    /// <summary>固定应答替身（值 / 结构）。</summary>
    public static IPortBinding Fixed(object? value) => new DelegateBinding(_ => value);

    /// <summary>固定应答替身（按输入计算固定结构，如多键 seed 表）。</summary>
    public static IPortBinding FixedBy(Func<IPortInput, object?> projector) => Recording(projector);

    /// <summary>回显替身：第一个位置实参经投影后即应答（输入决定输出）。</summary>
    public static IPortBinding Echo(Func<object?, object?> projector)
    {
        ArgumentNullException.ThrowIfNull(projector);
        return new DelegateBinding(input => projector(input.Args.Count > 0 ? input.Args[0] : null));
    }

    /// <summary>回显替身（按完整输入投影）。</summary>
    public static IPortBinding EchoBy(Func<IPortInput, object?> projector) => Recording(projector);

    /// <summary>负向守卫替身：被调用即失败（用例声明该端口不应被触达）。</summary>
    public static IPortBinding NotExpected() => new DelegateBinding(input => throw new PortCallNotExpectedException(input.PortKey));

    /// <summary>按序应答替身：按调用序吐出应答，耗尽即失败（驱动多轮 / 重试）。</summary>
    public static IPortBinding Sequence(params object?[] values) => SequenceOf(values);

    /// <summary>按序应答替身（序列来源可枚举）。</summary>
    public static IPortBinding SequenceOf(IEnumerable<object?> values)
    {
        ArgumentNullException.ThrowIfNull(values);
        var queue = new Queue<object?>(values);
        return new DelegateBinding(input =>
        {
            if (queue.Count == 0) throw new FakeSequenceExhaustedException(input.PortKey, 0);
            return queue.Dequeue();
        });
    }

    /// <summary>把委托收敛为绑定（内核唯一替身载体，避免 5 个平行私有类型）。</summary>
    private sealed class DelegateBinding : IPortBinding
    {
        private readonly Func<IPortInput, object?> _invoke;

        public DelegateBinding(Func<IPortInput, object?> invoke) => _invoke = invoke;

        public ValueTask<object?> InvokeAsync(IPortInput input, CancellationToken ct = default)
        {
            ArgumentNullException.ThrowIfNull(input);
            return ValueTask.FromResult(_invoke(input));
        }
    }
}
