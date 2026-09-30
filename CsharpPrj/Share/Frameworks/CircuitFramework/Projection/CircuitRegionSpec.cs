using Share.CircuitFramework.Symbols;

namespace Share.CircuitFramework.Projection;

/// <summary>
/// 一条区域端口映射：父图区域节点端口 ↔ 子计划端口。两端存在性由编译期判红（CFG-023）。
/// <list type="bullet">
///   <item>入映射：<see cref="ParentPort"/>（父区域节点的入端口） ← <see cref="ChildPort"/>（子计划的 EntryPort）；</item>
///   <item>出映射：<see cref="ChildPort"/>（子计划的 ExitPort） → <see cref="ParentPort"/>（父区域节点的出端口）。</item>
/// </list>
/// <para>
/// 两端口径与既有声明面一致：父端口 = 裸端口名（步骤 <c>InPorts</c>/<c>OutPorts</c>）；
/// 子<b>入口</b> = 裸端口名（<see cref="CircuitRecipeSpec.EntryPorts"/>）；
/// 子<b>出口</b> = <c>"NodeId.Port"</c> 全名（<see cref="CircuitRecipeSpec.ExitPorts"/>，与 <see cref="ExitBinding"/> 同格式）。
/// </para>
/// </summary>
/// <param name="ParentPort">父图区域节点侧端口名（裸名）。</param>
/// <param name="ChildPort">子计划侧端口名（入映射 = 裸 EntryPort 名；出映射 = <c>"NodeId.Port"</c>）。</param>
/// <param name="DataType">可选声明类型（如判定端口的 <c>bool</c>）：声明了才参与类型判据（框架配方域无类型推断）。</param>
public sealed record RegionPortBinding(string ParentPort, string ChildPort, string? DataType = null);

/// <summary>
/// 区域声明（区域化的声明面）：把「环 / 子计划」封在一个**普通节点**内部 ——
/// 父图仍是 DAG（CFG-002 不变、引擎零改动），子计划经
/// <see cref="Compiler.CircuitCompiler.TryCompile(CircuitRecipeSpec, out CompiledRecipe, out IReadOnlyList{CircuitDiagnostic})"/>
/// **同一编译入口**获得自己的编译期判据（R2）。
/// <para>G0 硬要求：父/子端口映射**必须进声明面**，使映射两端存在性在编译期判红 ——
/// 否则会把投影类错误漏回运行期，自破「编译绿 ⇒ 运行期不再有投影类错误」口径。</para>
/// </summary>
/// <param name="NodeId">承载该区域的父图步骤 Id（须已声明步骤；其入/出端口即区域端口）。</param>
/// <param name="RegionKind">区域节点 Kind（每实例唯一，如 <c>"{NodeId}#region"</c>；对齐「同 Kind 注册唯一」约束，不得反向放宽解析器）。</param>
/// <param name="Target">子计划身份引用（供 <see cref="Analysis.CircuitCallGraph"/> 跨层影响面使用）。</param>
/// <param name="Child">子计划声明（内联；自身可为含嵌套区域的 DAG）。</param>
/// <param name="InBindings">父入端口 ← 子入口 映射（须覆盖子计划全部 EntryPorts，否则漏喂）。</param>
/// <param name="OutBindings">子出口 → 父出端口 映射（未映射的子出口 = 该产物不回收，合法）。</param>
/// <param name="Plan">迭代计划（R1：必须带迭代上限）。</param>
public sealed record CircuitRegionSpec(
    string NodeId,
    string RegionKind,
    CalleeRef Target,
    CircuitRecipeSpec Child,
    IReadOnlyList<RegionPortBinding> InBindings,
    IReadOnlyList<RegionPortBinding> OutBindings,
    RegionIterationPlan Plan)
{
    /// <summary>区域节点 Kind 缺省后缀（<c>{NodeId}#region</c>：使同 Kind 唯一约束在同段内可满足）。</summary>
    public const string KindSuffix = "#region";

    /// <summary>
    /// 便捷构造（Kind 与子计划引用由 <paramref name="child"/> 派生，消除手抄漂移）。
    /// </summary>
    /// <param name="nodeId">承载节点 Id。</param>
    /// <param name="child">子计划声明。</param>
    /// <param name="plan">迭代计划。</param>
    /// <param name="inBindings">父入端口 ← 子入口 映射（缺省空 = 子计划无入口）。</param>
    /// <param name="outBindings">子出口 → 父出端口 映射（缺省空 = 不回收产物）。</param>
    /// <param name="regionKind">区域节点 Kind（缺省 <c>{nodeId}#region</c>）。</param>
    public static CircuitRegionSpec Of(
        string nodeId,
        CircuitRecipeSpec child,
        RegionIterationPlan plan,
        IReadOnlyList<RegionPortBinding>? inBindings = null,
        IReadOnlyList<RegionPortBinding>? outBindings = null,
        string? regionKind = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(nodeId);
        ArgumentNullException.ThrowIfNull(child);
        ArgumentNullException.ThrowIfNull(plan);

        return new CircuitRegionSpec(
            nodeId,
            string.IsNullOrWhiteSpace(regionKind) ? nodeId + KindSuffix : regionKind,
            new CalleeRef(child.CircuitId),
            child,
            inBindings ?? Array.Empty<RegionPortBinding>(),
            outBindings ?? Array.Empty<RegionPortBinding>(),
            plan);
    }
}
