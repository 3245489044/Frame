namespace Strategy.Observability.Ports;

using DataCenter.Protocol.Standards;

/// <summary>
/// 声明式**脚本绑定**（③ 兼容 facade；J3 随 ③ 家族一并退场）：只负责把 ③ 绑定声明
/// 归一为 ① 标准声明并转交**执行单源** <see cref="PortExecutor"/>（语义唯一实现点，无第二份编排）。
/// </summary>
public sealed class ScriptedBinding : IPortBinding
{
    private readonly PortExecutor _executor;

    /// <summary>
    /// 按 ③ 绑定声明构造（测试 / 旧装配入口）：声明经 <see cref="LegacyStandard"/> 归一到 ① 标准声明
    /// （唯一数据源）后交执行单源。无行为声明、载体冲突、引用形态残缺 → 即抛（fail-fast，防静默绑错）。
    /// </summary>
    public ScriptedBinding(ScriptedBindingSpec spec)
        : this(
            (spec ?? throw new ArgumentNullException(nameof(spec))).PortKey,
            LegacyStandard.Impl(spec),
            spec.Factories)
    {
    }

    /// <summary>按 ① 标准声明构造（求值面入口）：只认标准记录 + 工厂表，不识别 ③ 绑定声明家族。</summary>
    public ScriptedBinding(
        string portKey, PortImplStandard impl,
        IReadOnlyDictionary<string, Func<IReadOnlyList<object?>, object?>>? factories = null)
        => _executor = new PortExecutor(portKey, impl, factories);

    /// <summary>求值（每调用一个上下文；执行语义见 <see cref="PortExecutor"/>）。</summary>
    public ValueTask<object?> InvokeAsync(IPortInput input, CancellationToken ct = default)
        => ValueTask.FromResult(_executor.Run(input));

    /// <summary>
    /// 最近一次求值的诊断记录（W1：步骤 <c>when</c> / 遍历 <c>where</c> 求值为 ⊥ ⇒ 不执行且留痕，不静默）。
    /// 绑定实例在端口目录中为单例、可被并发调用 —— 记录按异步执行流隔离，防调用间最后写胜串台。
    /// </summary>
    public IReadOnlyList<string> LastNotes => _executor.LastNotes;
}
