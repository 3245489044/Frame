namespace Strategy.Observability.Ports;

/// <summary>
/// 端口绑定（CF-4 薄适配壳）：<see cref="IPortBinding"/> 的**接缝实现** —— 执行经注入的
/// <see cref="IPortExecutionBackend"/> 外置；**未注入后端**时回退旧路径（<see cref="PortEvaluatorBinding"/>）。
/// <para>
/// 构造期解析 / 校验复用 <see cref="PortEvaluatorBinding"/>（同一处解析 → 装配期 fail-fast / <c>Unbound</c>
/// 记账零漂移），故「未注入后端」与旧绑定**逐字等价**（0 行为变更）；仅「已注入后端」才切换执行面。
/// </para>
/// <para>
/// 判据载体 = 结构事实：<see cref="UsesSeam"/>（后端非空 = 执行面已外置），供诊断 / 守卫消费，
/// 不依赖执行者叙述。回退 = 去掉注入（一版内可逆）。
/// </para>
/// <para>
/// 留痕口径：回退路径透传旧绑定 <see cref="LastNotes"/>；接缝路径的留痕由后端自持（本件不复制执行语义）。
/// </para>
/// </summary>
public sealed class PortCircuitBinding : IPortBinding
{
    private readonly PortSpec _spec;
    private readonly PortInjection _injection;
    private readonly PortEvaluatorBinding _fallback;
    private readonly IPortExecutionBackend? _backend;

    /// <summary>
    /// 按规格构造绑定（<paramref name="backend"/> 缺省 null = 未接缝，回退旧路径；
    /// <paramref name="evaluator"/> 缺省 = <see cref="PortEvaluator.Shared"/>；
    /// <paramref name="injection"/> 缺省 = 空注入袋）。
    /// </summary>
    public PortCircuitBinding(
        PortSpec spec,
        IPortExecutionBackend? backend = null,
        PortEvaluator? evaluator = null,
        PortInjection? injection = null)
    {
        _spec = PortGuard.Required(spec, nameof(spec));
        _injection = injection ?? PortInjection.Empty;

        // 构造期解析 / 校验：与旧绑定同一实现（fail-fast 口径一致 ⇒ 装配期记账零漂移）。
        _fallback = new PortEvaluatorBinding(_spec, evaluator, _injection);
        _backend = backend;
    }

    /// <summary>执行面是否已外置到接缝（结构事实：后端非空）。</summary>
    public bool UsesSeam => _backend is not null;

    /// <summary>最近一次求值的诊断记录（回退路径透传旧绑定；接缝路径由后端自持，此处为空集）。</summary>
    public IReadOnlyList<string> LastNotes => _fallback.LastNotes;

    /// <inheritdoc />
    public ValueTask<object?> InvokeAsync(IPortInput input, CancellationToken ct = default)
        => _backend is null
            ? _fallback.InvokeAsync(input, ct)
            : _backend.ExecuteAsync(_spec, input, _injection, ct);
}
