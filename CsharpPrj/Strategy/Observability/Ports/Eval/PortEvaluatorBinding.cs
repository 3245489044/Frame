namespace Strategy.Observability.Ports;

/// <summary>
/// 引擎对目录的**唯一接线面**（R3-1）：把 <see cref="PortSpec"/> 适配为 <see cref="IPortBinding"/>，
/// 使引擎绑定与旧绑定在 <see cref="PortCatalog"/> 面不可区分（装饰链 <c>Decorate</c> / 记录零改动）。
/// <para>
/// 构造期即为**解析期**：与旧反射绑定同语义（目标类型 / 方法不可解析即 fail-fast），
/// 从而装配期失败仍按既有口径记入 <c>Unbound</c>（fail-safe 记账不变）。
/// </para>
/// </summary>
public sealed class PortEvaluatorBinding : IPortBinding
{
    private readonly PortSpec _spec;
    private readonly PortEvaluator _evaluator;

    /// <summary>按规格构造绑定（<paramref name="evaluator"/> 缺省 = <see cref="PortEvaluator.Shared"/>）。</summary>
    public PortEvaluatorBinding(PortSpec spec, PortEvaluator? evaluator = null)
    {
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
        _evaluator = evaluator ?? PortEvaluator.Shared;

        // 构造期解析：与 ReflectiveBinding 构造语义逐字对齐（fail-fast → 装配期 Unbound 记账一致）。
        if (_spec.Program.Count == 1 && _spec.Program[0].Op == PortOp.Call)
        {
            var program = _spec.Program[0];
            _ = PortCall.Resolve(new PortBindingSpec(
                _spec.PortKey, program.Target ?? string.Empty, program.Method ?? string.Empty, program.Args));
        }
    }

    /// <summary>求值（每调用一个上下文；上下文承载定位与留痕设施）。</summary>
    public ValueTask<object?> InvokeAsync(IPortInput input, CancellationToken ct = default)
        => _evaluator.EvaluateAsync(_spec, input, new EvalContext(_spec.PortKey), ct);
}
