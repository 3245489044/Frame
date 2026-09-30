using Share.CircuitFramework.Compiler;
using Share.CircuitFramework.Core;
using Share.CircuitFramework.Projection;

namespace Share.CircuitFramework.Execution;

/// <summary>
/// 区域节点基座：把「环 / 子计划」封在一个**普通节点**内部 —— 父图仍是 DAG（引擎零改动），
/// 体内按 <see cref="RegionIterationPlan"/> 驱动子计划的编译产物（<see cref="CompiledRecipe"/>）。
/// <para>
/// 取值通道与既有节点一致：父入端口 → 子 EntryPort（按 <see cref="RegionPortBinding"/> 映射）经
/// <c>entryInputs</c> 供给；子 ExitPort → 父出端口回投为本次节点输出。
/// 子计划每次迭代 = 一次独立 <c>CircuitEngine.ExecuteAsync</c> ⇒ 上下文天然隔离（跨迭代状态由本节点自身持有，不引入第二条数据通道）。
/// </para>
/// <para>
/// 迭代有界（R1）：上限来自声明面 <see cref="RegionIterationPlan.MaxIterations"/>；达上限按**正常收口**处理
/// （输出末次迭代值），不抛 —— 框架不承担停机判定（停机责任外置）。
/// </para>
/// <para>
/// Kind 唯一：区域节点 Kind 由声明面给出（每实例唯一，如 <c>"{NodeId}#region"</c>），
/// 使「同 Kind 注册唯一」约束在同段内可满足（不得反向放宽解析器）。
/// </para>
/// </summary>
public sealed class RegionNodeBase : ICircuitNode
{
    private readonly CompiledRecipe _child;
    private readonly IReadOnlyList<RegionPortBinding> _inBindings;
    private readonly IReadOnlyList<RegionPortBinding> _outBindings;
    private readonly RegionIterationPlan _plan;
    private readonly Func<string, ICircuitNode> _resolveChild;

    /// <param name="nodeType">节点 Kind（与声明面 <see cref="CircuitRegionSpec.RegionKind"/> 一致）。</param>
    /// <param name="child">子计划编译产物（须经唯一编译入口产出）。</param>
    /// <param name="plan">迭代计划（有界；缺失即抛，防无界执行）。</param>
    /// <param name="inBindings">父入端口 ← 子入口 映射。</param>
    /// <param name="outBindings">子出口 → 父出端口 映射。</param>
    /// <param name="resolveChild">子图节点 Kind → 黑盒实现（子计划内层解析；通常与父图同源）。</param>
    public RegionNodeBase(
        string nodeType,
        CompiledRecipe child,
        RegionIterationPlan plan,
        IReadOnlyList<RegionPortBinding> inBindings,
        IReadOnlyList<RegionPortBinding> outBindings,
        Func<string, ICircuitNode> resolveChild)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);
        NodeType = nodeType;
        _child = child ?? throw new ArgumentNullException(nameof(child));
        _plan = plan ?? throw new ArgumentNullException(nameof(plan));
        _inBindings = inBindings ?? throw new ArgumentNullException(nameof(inBindings));
        _outBindings = outBindings ?? throw new ArgumentNullException(nameof(outBindings));
        _resolveChild = resolveChild ?? throw new ArgumentNullException(nameof(resolveChild));
    }

    /// <inheritdoc />
    public string NodeType { get; }

    /// <summary>
    /// 由区域声明构造（**子计划一律经 <see cref="CompiledRecipeRegistry"/>**：内容指纹缓存 + 唯一编译入口）。
    /// </summary>
    public static RegionNodeBase For(CircuitRegionSpec region, Func<string, ICircuitNode> resolveChild)
    {
        ArgumentNullException.ThrowIfNull(region);
        ArgumentNullException.ThrowIfNull(region.Child);

        return new RegionNodeBase(
            region.RegionKind,
            CompiledRecipeRegistry.GetOrCompile(region.Child),
            region.Plan,
            region.InBindings,
            region.OutBindings,
            resolveChild);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, object?>> ExecuteAsync(
        IReadOnlyDictionary<string, object?> inputs, CancellationToken cancellationToken)
    {
        switch (_plan.Kind)
        {
            case RegionIterationKind.Once:
                return BuildOutputs(await RunChildAsync(inputs, cancellationToken).ConfigureAwait(false));

            case RegionIterationKind.FixedRepeats:
                return BuildOutputs(await RunFixedAsync(inputs, cancellationToken).ConfigureAwait(false));

            case RegionIterationKind.WhileTrue:
                return BuildOutputs(await RunWhileAsync(inputs, cancellationToken).ConfigureAwait(false));

            case RegionIterationKind.UntilTrue:
                return BuildOutputs(await RunUntilAsync(inputs, cancellationToken).ConfigureAwait(false));

            default:
                // 未落地的迭代方式：显式拒绝，不静默退化为单趟（新受控词接入前保持该负对照）。
                throw new NotSupportedException(
                    $"区域节点 '{NodeType}' 的迭代方式 {_plan.Kind} 尚未落地执行面（声明可编译，执行面后续批次接入）。");
        }
    }

    /// <summary>单次子计划执行（父入端口 → 子入口；一次独立 <c>CircuitEngine.ExecuteAsync</c> ⇒ 上下文天然隔离）。</summary>
    private Task<IReadOnlyDictionary<string, object?>> RunChildAsync(
        IReadOnlyDictionary<string, object?> inputs, CancellationToken cancellationToken)
        => CircuitEngine.ExecuteAsync(
            _child.Circuit, _child.Plan, _resolveChild, BuildEntry(inputs), cancellationToken);

    /// <summary>有界计次重复（<c>Repeats</c>；0 = 零次迭代，合法）。</summary>
    private async Task<IReadOnlyDictionary<string, object?>?> RunFixedAsync(
        IReadOnlyDictionary<string, object?> inputs, CancellationToken cancellationToken)
    {
        IReadOnlyDictionary<string, object?>? last = null;
        var repeats = Math.Max(0, _plan.Repeats);
        for (var i = 0; i < repeats; i++)
            last = await RunChildAsync(inputs, cancellationToken).ConfigureAwait(false);
        return last;
    }

    /// <summary>
    /// 上判定条件迭代（<c>WhileTrue</c>）：判定为真时执行。首轮判定 = 父入端口键（<see cref="RegionIterationPlan.JudgePort"/>）；
    /// 迭代间判定由出映射反馈自持（出映射中 <c>ParentPort == JudgePort</c> 的项提供下一轮判定值）。
    /// </summary>
    private async Task<IReadOnlyDictionary<string, object?>?> RunWhileAsync(
        IReadOnlyDictionary<string, object?> inputs, CancellationToken cancellationToken)
    {
        var max = MaxIterations();
        var judge = SeedJudge(inputs);
        IReadOnlyDictionary<string, object?>? last = null;
        for (var i = 0; i < max && judge; i++)
        {
            last = await RunChildAsync(inputs, cancellationToken).ConfigureAwait(false);
            judge = FeedbackJudge(last, judge);
        }
        return last;
    }

    /// <summary>后判定条件迭代（<c>UntilTrue</c>）：先执行一次，判定为真即停（do-while 语义）。</summary>
    private async Task<IReadOnlyDictionary<string, object?>?> RunUntilAsync(
        IReadOnlyDictionary<string, object?> inputs, CancellationToken cancellationToken)
    {
        var max = MaxIterations();
        var judge = SeedJudge(inputs);
        IReadOnlyDictionary<string, object?>? last = null;
        for (var i = 0; i < max; i++)
        {
            last = await RunChildAsync(inputs, cancellationToken).ConfigureAwait(false);
            judge = FeedbackJudge(last, judge);
            if (judge) break;
        }
        return last;
    }

    /// <summary>首轮判定：父入端口键（缺失 ⇒ false = 不进入循环，不伪造）。</summary>
    private bool SeedJudge(IReadOnlyDictionary<string, object?> inputs)
        => _plan.JudgePort is { Length: > 0 } port
            ? Judge(inputs.TryGetValue(port, out var value) ? value : null, port)
            : false;

    /// <summary>迭代间反馈：出映射中 <c>ParentPort == JudgePort</c> 的项提供下一轮判定（无则保持当前值）。</summary>
    private bool FeedbackJudge(IReadOnlyDictionary<string, object?>? childResult, bool current)
    {
        if (childResult is null || _plan.JudgePort is not { Length: > 0 } judge)
            return current;

        foreach (var binding in _outBindings)
        {
            if (!string.Equals(binding.ParentPort, judge, StringComparison.Ordinal)) continue;
            if (childResult.TryGetValue(binding.ChildPort, out var value))
                return Judge(value, judge);
            break;
        }
        return current;
    }

    /// <summary>判定值归一（载体须为结构化布尔事实；非布尔即抛，不静默误判）。</summary>
    private static bool Judge(object? value, string judgePort) => value switch
    {
        bool b => b,
        null => false,
        _ => throw new InvalidOperationException(
            $"区域条件迭代的判定端口 '{judgePort}' 值不是布尔（实际 {value.GetType().Name}）：判定载体须为结构化布尔事实。"),
    };

    /// <summary>有效迭代上限（按受控词解析；条件迭代上限由 CFG-022 保证 &gt; 0）。</summary>
    private int MaxIterations() => _plan.Kind switch
    {
        RegionIterationKind.Once => 1,
        RegionIterationKind.FixedRepeats => _plan.MaxIterations ?? Math.Max(0, _plan.Repeats),
        RegionIterationKind.WhileTrue or RegionIterationKind.UntilTrue => _plan.MaxIterations ?? 0,
        _ => throw new NotSupportedException(
            $"区域节点 '{NodeType}' 的迭代方式 {_plan.Kind} 尚未落地执行面（声明可编译，执行面后续批次接入）。"),
    };

    /// <summary>父入端口 → 子 EntryPort（缺失即跳过，与引擎入口供给同口径：不伪造 null）。</summary>
    private IReadOnlyDictionary<string, object?> BuildEntry(IReadOnlyDictionary<string, object?> inputs)
    {
        if (_inBindings.Count == 0) return EmptyEntry;
        var entry = new Dictionary<string, object?>(_inBindings.Count, StringComparer.Ordinal);
        foreach (var binding in _inBindings)
            if (inputs.TryGetValue(binding.ParentPort, out var value))
                entry[binding.ChildPort] = value;
        return entry;
    }

    /// <summary>子出口 → 父出端口；零次迭代（<c>Repeats == 0</c>）时按声明给出 null，避免父侧端口静默缺席。</summary>
    private IReadOnlyDictionary<string, object?> BuildOutputs(IReadOnlyDictionary<string, object?>? childResult)
    {
        if (_outBindings.Count == 0) return EmptyOutputs;
        var outputs = new Dictionary<string, object?>(_outBindings.Count, StringComparer.Ordinal);
        foreach (var binding in _outBindings)
        {
            outputs[binding.ParentPort] = childResult is not null
                && childResult.TryGetValue(binding.ChildPort, out var value)
                    ? value
                    : null;
        }
        return outputs;
    }

    private static readonly IReadOnlyDictionary<string, object?> EmptyEntry =
        new Dictionary<string, object?>(0, StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, object?> EmptyOutputs =
        new Dictionary<string, object?>(0, StringComparer.Ordinal);
}
