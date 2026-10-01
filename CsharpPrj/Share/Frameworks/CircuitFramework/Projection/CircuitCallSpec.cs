using Share.CircuitFramework.Symbols;

namespace Share.CircuitFramework.Projection;

/// <summary>
/// 调用声明（R2 声明面）：承载节点（<c>CallerNodeId</c>）调用子计划 <c>Child</c>。
/// 投影为 <c>Symbols.CallSymbol</c>（调用边可见）；执行面 = <c>Execution.SubCircuitNode</c>（一次子电路调用，
/// 绑定/出口回投与区域同口径）；子计划可解析性与父侧端口映射由编译期 <c>CFG-027</c> 判红。
/// </summary>
/// <param name="CallerNodeId">调用者节点 Id（须已声明步骤；其节点 Kind 须解析到 SubCircuitNode）。</param>
/// <param name="CalleeKind">被调 Kind（执行面解析键 = SubCircuitNode.NodeType）。</param>
/// <param name="Target">被调电路身份引用（跨版本唯一；供调用图/影响面分析）。</param>
/// <param name="Child">子计划声明（内联；自身可为含嵌套区域/调用的 DAG）。</param>
/// <param name="InBindings">父入端口 ← 子入口 映射（须覆盖子计划全部 EntryPorts，否则漏喂）。</param>
/// <param name="OutBindings">子出口 → 父出端口 映射（未映射的子出口 = 该产物不回收，合法）。</param>
public sealed record CircuitCallSpec(
    string CallerNodeId,
    string CalleeKind,
    CalleeRef Target,
    CircuitRecipeSpec Child,
    IReadOnlyList<RegionPortBinding>? InBindings = null,
    IReadOnlyList<RegionPortBinding>? OutBindings = null)
{
    /// <summary>便捷构造（目标引用由 <paramref name="child"/> 派生，消除手抄漂移）。</summary>
    public static CircuitCallSpec Of(
        string callerNodeId,
        string calleeKind,
        CircuitRecipeSpec child,
        IReadOnlyList<RegionPortBinding>? inBindings = null,
        IReadOnlyList<RegionPortBinding>? outBindings = null)
    {
        ArgumentNullException.ThrowIfNull(child);
        return new CircuitCallSpec(callerNodeId, calleeKind, new CalleeRef(child.CircuitId), child, inBindings, outBindings);
    }
}
