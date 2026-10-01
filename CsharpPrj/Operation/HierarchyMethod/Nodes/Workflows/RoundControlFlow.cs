namespace Operation.HierarchyMethod.Nodes.Workflows
{
    /// <summary>
    /// 分支声明（R3 中立原语，加法式扩展）：判定键为真 → <see cref="ThenNodeId"/> 激活、<see cref="ElseNodeId"/> 抑制；为假则反之。
    /// <para>
    /// **中立原语**：禁止写成框架类型（本工程不得新开 → <c>Share.CircuitFramework</c> 引用边）；
    /// 由 <c>RoundFlowSpecMapper</c> 映射为电路配方分支声明（框架只承载结构，阈值/语义归宿主）。
    /// </para>
    /// </summary>
    /// <param name="NodeId">判定承载节点（步骤 EffectiveNodeId）。</param>
    /// <param name="JudgeKey">判定键（结构化数据键：<c>NodeId.Port</c> / 入口名 / <c>$...</c>）。</param>
    /// <param name="ThenNodeId">判定为真时激活的目标节点。</param>
    /// <param name="ElseNodeId">判定为假时激活的目标节点（可空 = 单边分支）。</param>
    public sealed record RoundBranch(string NodeId, string JudgeKey, string ThenNodeId, string? ElseNodeId = null);

    /// <summary>
    /// 多路分派声明（R3 中立原语）：选择键取值命中 <see cref="Routes"/> 目标则激活，未命中则 <see cref="DefaultNodeId"/> 激活（互斥）。
    /// </summary>
    /// <param name="NodeId">分派承载节点（步骤 EffectiveNodeId）。</param>
    /// <param name="SelectorKey">选择键（结构化数据键）。</param>
    /// <param name="Routes">取值 → 目标节点。</param>
    /// <param name="DefaultNodeId">未命中任何 route 时激活的默认目标（可空）。</param>
    public sealed record RoundSwitch(
        string NodeId,
        string SelectorKey,
        IReadOnlyDictionary<string, string> Routes,
        string? DefaultNodeId = null);

    /// <summary>调用端口映射（R3 中立原语）：父端口 ↔ 子端口（入映射 = 父入 ← 子入口；出映射 = 子出口 → 父出）。</summary>
    public sealed record RoundPortBinding(string ParentPort, string ChildPort);

    /// <summary>
    /// 调用声明（R3 中立原语）：承载节点（<see cref="CallerNodeId"/>）调用子骨架 <see cref="Child"/>；
    /// 由 <c>RoundFlowSpecMapper</c> 映射为电路配方调用声明（子骨架就地递归映射为子电路配方）。
    /// </summary>
    /// <param name="CallerNodeId">调用者节点（步骤 EffectiveNodeId；其步骤 Kind 须解析到子电路调用节点）。</param>
    /// <param name="Child">被调子骨架（就地映射为子电路配方）。</param>
    /// <param name="CalleeKind">被调 Kind（执行面解析键）；缺省 = <see cref="CallerNodeId"/>（承载节点 Kind 即被调 Kind）。</param>
    /// <param name="InBindings">父入端口 ← 子入口 映射。</param>
    /// <param name="OutBindings">子出口 → 父出端口 映射。</param>
    public sealed record RoundCall(
        string CallerNodeId,
        RoundSpec Child,
        string? CalleeKind = null,
        IReadOnlyList<RoundPortBinding>? InBindings = null,
        IReadOnlyList<RoundPortBinding>? OutBindings = null)
    {
        /// <summary>被调 Kind（缺省 = 承载节点 Id，使承载步骤的 Kind 与被调 Kind 一致）。</summary>
        public string EffectiveCalleeKind => string.IsNullOrWhiteSpace(CalleeKind) ? CallerNodeId : CalleeKind;
    }
}
