using Share.CircuitFramework.Compiler;
using Share.CircuitFramework.Core;
using Share.CircuitFramework.Projection;

namespace Share.CircuitFramework.Execution;

/// <summary>
/// 子电路调用节点（R2）：一次子电路调用 —— <see cref="RegionNodeBase"/> 的 <c>Once</c> 为其特例。
/// <para>
/// 取值通道与区域一致：父入端口 → 子 EntryPort（按 <see cref="RegionPortBinding"/> 映射）经 entryInputs 供给；
/// 子 ExitPort → 父出端口回投。子计划一律经 <see cref="CompiledRecipeRegistry"/>（内容指纹缓存 + 唯一编译入口）。
/// </para>
/// </summary>
public sealed class SubCircuitNode : ICircuitNode
{
    private readonly CompiledRecipe _child;
    private readonly IReadOnlyList<RegionPortBinding> _inBindings;
    private readonly IReadOnlyList<RegionPortBinding> _outBindings;
    private readonly Func<string, ICircuitNode> _resolveChild;

    /// <param name="nodeType">节点 Kind（与调用声明 CalleeKind 一致；执行层按 Kind 解析）。</param>
    /// <param name="child">子计划编译产物（须经唯一编译入口产出）。</param>
    /// <param name="inBindings">父入端口 ← 子入口 映射。</param>
    /// <param name="outBindings">子出口 → 父出端口 映射。</param>
    /// <param name="resolveChild">子图节点 Kind → 黑盒实现。</param>
    public SubCircuitNode(
        string nodeType,
        CompiledRecipe child,
        IReadOnlyList<RegionPortBinding> inBindings,
        IReadOnlyList<RegionPortBinding> outBindings,
        Func<string, ICircuitNode> resolveChild)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeType);
        NodeType = nodeType;
        _child = child ?? throw new ArgumentNullException(nameof(child));
        _inBindings = inBindings ?? Array.Empty<RegionPortBinding>();
        _outBindings = outBindings ?? Array.Empty<RegionPortBinding>();
        _resolveChild = resolveChild ?? throw new ArgumentNullException(nameof(resolveChild));
    }

    /// <inheritdoc />
    public string NodeType { get; }

    /// <summary>由调用声明构造（子计划一律经 <see cref="CompiledRecipeRegistry"/> 取得）。</summary>
    public static SubCircuitNode For(CircuitCallSpec call, Func<string, ICircuitNode> resolveChild)
    {
        ArgumentNullException.ThrowIfNull(call);
        return new SubCircuitNode(
            call.CalleeKind,
            CompiledRecipeRegistry.GetOrCompile(call.Child),
            call.InBindings ?? Array.Empty<RegionPortBinding>(),
            call.OutBindings ?? Array.Empty<RegionPortBinding>(),
            resolveChild);
    }

    /// <inheritdoc />
    public async Task<IReadOnlyDictionary<string, object?>> ExecuteAsync(
        IReadOnlyDictionary<string, object?> inputs, CancellationToken cancellationToken)
        => BuildOutputs(_outBindings,
            await RunChildAsync(_child, _resolveChild, _inBindings, inputs, cancellationToken).ConfigureAwait(false));

    /// <summary>
    /// 单次子计划执行共享内核（R2/D4）：父入端口 → 子 EntryPort 供给，返回**子电路出口字典**（不回投父出端口）。
    /// <para>与 <see cref="RegionNodeBase"/> 共用同一份绑定/取值/驱动实现（Once 分支与迭代共用），消除整类复制。</para>
    /// </summary>
    internal static Task<IReadOnlyDictionary<string, object?>> RunChildAsync(
        CompiledRecipe child,
        Func<string, ICircuitNode> resolveChild,
        IReadOnlyList<RegionPortBinding> inBindings,
        IReadOnlyDictionary<string, object?> inputs,
        CancellationToken cancellationToken)
        => CircuitEngine.ExecuteAsync(
            child.Circuit, child.Plan, resolveChild, BuildEntry(inBindings, inputs), cancellationToken);

    /// <summary>父入端口 → 子 EntryPort（缺失即跳过，与引擎入口供给同口径：不伪造 null）。</summary>
    internal static IReadOnlyDictionary<string, object?> BuildEntry(
        IReadOnlyList<RegionPortBinding> inBindings, IReadOnlyDictionary<string, object?> inputs)
    {
        if (inBindings.Count == 0) return Empty;
        var entry = new Dictionary<string, object?>(inBindings.Count, StringComparer.Ordinal);
        foreach (var binding in inBindings)
            if (inputs.TryGetValue(binding.ParentPort, out var value))
                entry[binding.ChildPort] = value;
        return entry;
    }

    /// <summary>子出口 → 父出端口；缺失的子出口按声明给出 null，避免父侧端口静默缺席。</summary>
    internal static IReadOnlyDictionary<string, object?> BuildOutputs(
        IReadOnlyList<RegionPortBinding> outBindings, IReadOnlyDictionary<string, object?>? childResult)
    {
        if (outBindings.Count == 0) return Empty;
        var outputs = new Dictionary<string, object?>(outBindings.Count, StringComparer.Ordinal);
        foreach (var binding in outBindings)
            outputs[binding.ParentPort] = childResult is not null
                && childResult.TryGetValue(binding.ChildPort, out var value) ? value : null;
        return outputs;
    }

    private static readonly IReadOnlyDictionary<string, object?> Empty =
        new Dictionary<string, object?>(0, StringComparer.Ordinal);
}
