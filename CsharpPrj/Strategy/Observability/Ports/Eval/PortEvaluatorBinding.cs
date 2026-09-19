namespace Strategy.Observability.Ports;

using DataCenter.Protocol.Standards;

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
    private readonly PortInjection _injection;
    private readonly PortExecutor? _executor;

    /// <summary>
    /// 按规格构造绑定（<paramref name="evaluator"/> 缺省 = <see cref="PortEvaluator.Shared"/>；
    /// <paramref name="injection"/> 缺省 = 空注入袋 —— 声明了 <c>dep.factory</c> 的端口须经装配侧注入工厂表）。
    /// </summary>
    public PortEvaluatorBinding(PortSpec spec, PortEvaluator? evaluator = null, PortInjection? injection = null)
    {
        _spec = spec ?? throw new ArgumentNullException(nameof(spec));
        _evaluator = evaluator ?? PortEvaluator.Shared;
        _injection = injection ?? PortInjection.Empty;

        // 构造期解析：reflective 形态（无载体的单条 Call）与 ReflectiveBinding 构造语义逐字对齐
        // （fail-fast → 装配期 Unbound 记账一致）。声明驱动的其它形态在求值期按指令逐一解析。
        if (_spec.Program.Count == 1 && _spec.Program[0].Op == PortOp.Call && _spec.Program[0].Operand is null)
        {
            var program = _spec.Program[0];
            _ = PortCall.Resolve(new PortBindingSpec(
                _spec.PortKey, program.Target ?? string.Empty, program.Method ?? string.Empty, program.Args));
            return;
        }

        // 脚本族签名面（Deliver 非空 = 带载体 / 输入绑定 / 作用域 / 场景）：构造期建**执行单源**
        // （声明校验与旧绑定类构造同口径 ⇒ 装配期 fail-fast / Unbound 记账零漂移）；求值走同一实现。
        if (_spec.Deliver is not null)
        {
            _executor = new PortExecutor(_spec, _injection);
            return;
        }

        // 组合族（J2）：调用解算与 ComposedBinding 构造同口径（声明漂移在装配期即 fail-fast → Unbound 记账一致）。
        if (!_spec.Program.Any(instruction => instruction.Op == PortOp.Call && instruction.Operand is CallStandard)) return;

        var declared = PortDependency.Declare(_spec.Program);
        foreach (var instruction in _spec.Program)
            if (instruction.Op == PortOp.Call && instruction.Operand is CallStandard call)
                _ = PortComposedCall.Resolve(declared, _spec.PortKey, call);
    }

    /// <summary>求值（每调用一个上下文；上下文承载定位与留痕设施）。</summary>
    public ValueTask<object?> InvokeAsync(IPortInput input, CancellationToken ct = default)
        => _executor is null
            ? _evaluator.EvaluateAsync(_spec, input, new EvalContext(_spec.PortKey), _injection, ct)
            : ValueTask.FromResult(_executor.Run(input));
}
