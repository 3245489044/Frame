using Share.CircuitFramework.Core;
using Share.CircuitFramework.Symbols;
using Ckt = Share.CircuitFramework.Core.Circuit;

namespace Share.CircuitFramework.Projection;

/// <summary>
/// 装配配方投影层（读侧投影）：把声明式装配配方 <see cref="CircuitRecipeSpec"/> 投影为电路 <see cref="Ckt"/> ——
/// 步骤 = 节点 + 具名端口，产物→依赖 = Wire，入口 = EntrySymbol，最终产物 = ExitSymbol。
/// 被投影电路可交给 CircuitValidator / CircuitCompiler / CircuitCallGraph 做校验与影响面分析；
/// 只装配电路、不回写业务代码（沿「读侧投影不回写」纪律）。
/// </summary>
public static class CircuitRecipeProjector
{
    /// <summary>递归基例的空终止输出（RecursionSymbol.BaseCase.Emit 的缺省；终止值由执行面给出）。</summary>
    private static readonly IReadOnlyDictionary<string, string> EmptyEmit =
        new Dictionary<string, string>(0);

    /// <summary>把声明式装配配方投影为电路。</summary>
    public static Ckt Project(CircuitRecipeSpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);

        var circuit = new Ckt(spec.CircuitId);
        AppendSteps(circuit, spec.Steps);
        AppendFlows(circuit, spec.Flows);

        // 入口绑定（外部产物，如 Config）→ EntrySymbol
        foreach (var e in spec.EntryPorts)
            circuit.Append(new EntrySymbol { Port = e });

        // 最终产物（NodeId.Port）→ ExitSymbol
        foreach (var ex in spec.ExitPorts)
            circuit.Append(new ExitSymbol { Port = ex });

        // 常量注入（B3-1）→ LiteralSymbol（执行后端直接注入端口值，不经 Wire/Entry）
        foreach (var literal in spec.Literals ?? Array.Empty<CircuitLiteralSpec>())
            circuit.Append(new LiteralSymbol
            {
                TargetNodeId = literal.NodeId,
                Port = literal.Port,
                Value = literal.Value,
                ValueType = literal.Value?.GetType().Name,
            });

        // 区域声明 → 子电路镜像符号：使 CircuitCallGraph 的跨层影响面（谁调用了子图）在电路层可用；
        // 执行面 = Execution.RegionNodeBase 在父图普通节点内驱动子计划（父图拓扑不含环）。
        foreach (var region in spec.Regions ?? Array.Empty<CircuitRegionSpec>())
        {
            circuit.Append(new SubCircuitSymbol { NodeId = region.NodeId, Target = region.Target });

            // G1 有界重复 ⇒ Loop 镜像（符号域视图与区域声明同形；能力表 Loop 的「声明 ↔ 执行」对拍面）。
            // Body = **纯投影镜像**（不经注册表、不参与执行）：执行真源 = CompiledRecipeRegistry 的内容指纹键，
            // 镜像只供 CFG-101 / CircuitCallGraph / 离线审阅；「镜像不得被执行面消费」由 CS10-e 结构判据锁死。
            if (region.Plan is { Kind: RegionIterationKind.FixedRepeats } plan && region.Child is not null)
            {
                circuit.Append(new LoopSymbol
                {
                    NodeId = region.NodeId,
                    Repeats = plan.Repeats,
                    Body = Project(region.Child),
                });
            }

            // G2 条件迭代 ⇒ WhileCond 镜像（声明文案 = 判定端口键，同源对账；执行面 = RegionNodeBase）。
            // PostCondition 仅后判定（UntilTrue）填写，与上判定（WhileTrue）区分。
            if (region.Plan is { Kind: RegionIterationKind.WhileTrue or RegionIterationKind.UntilTrue } cond)
            {
                circuit.Append(new WhileCondSymbol
                {
                    NodeId = region.NodeId,
                    Condition = cond.JudgePort ?? string.Empty,
                    PostCondition = cond.Kind == RegionIterationKind.UntilTrue ? cond.JudgePort : null,
                });
            }

            // F3 递归执行化（M2 降级）⇒ RecursionSymbol + BaseCaseSymbol 镜像：自引用有界递归的符号视图。
            // 回边/回连 = 同一基例端口（须在承载节点入/出端口双端声明，见 RegionValidator CFG-020）；
            // 策略 = Iterative（环封在区域节点内，父图仍为 DAG）；执行面 = RegionNodeBase.RunRecursionAsync。
            if (region.Plan is { Kind: RegionIterationKind.Recursion } recursion
                && recursion.JudgePort is { Length: > 0 } baseCasePort)
            {
                circuit.Append(new RecursionSymbol
                {
                    NodeId = region.NodeId,
                    BackEdge = new PortRef(region.NodeId, baseCasePort),
                    LoopBack = new PortRef(region.NodeId, baseCasePort),
                    BaseCase = new BaseCaseSymbol { Predicate = baseCasePort, Emit = EmptyEmit },
                    Strategy = RecursionStrategy.Iterative,
                });
            }
        }

        // 控制边声明 → SequenceSymbol 镜像：前驱只约束后继先后、不经数据键（无法表达为 Wire），
        // 以符号域视图使「谁调谁」在电路层可见（编译校验 / 静态分析 / CFG-101 对账）；
        // 执行序仍由 Wire + 声明序承担（能力表不含 Sequence ⇒ 该缺口由 CFG-101 可见化，不静默）。
        foreach (var edge in spec.ControlEdges ?? Array.Empty<CircuitControlEdgeSpec>())
            circuit.Append(new SequenceSymbol { NodeIds = new[] { edge.FromNode, edge.ToNode } });

        // 容错策略声明（G4）→ Retry / Fallback / TryGuard 符号：使机制在电路层可见（编译期 CFG-024 判目标存在，
        // 执行期由 CircuitRuntimeIndex.Build 合并为 NodeFailurePolicy 消费）。
        foreach (var retry in spec.Retries ?? Array.Empty<CircuitRetrySpec>())
            circuit.Append(new RetrySymbol
            {
                NodeId = retry.NodeId,
                MaxAttempts = retry.MaxAttempts,
                BackoffBaseMs = retry.BackoffBaseMs,
            });

        foreach (var fallback in spec.Fallbacks ?? Array.Empty<CircuitFallbackSpec>())
            circuit.Append(new FallbackSymbol
            {
                PrimaryNodeId = fallback.PrimaryNodeId,
                AlternativeNodeId = fallback.AlternativeNodeId,
            });

        foreach (var guard in spec.Guards ?? Array.Empty<CircuitGuardSpec>())
            circuit.Append(new TryGuardSymbol { ProtectedNodeIds = guard.ProtectedNodeIds });

        // R1 分支/分派声明 → BranchSymbol/SwitchSymbol：判定键随符号落进电路，
        // 执行面（CircuitRuntimeIndex）据 JudgeKey/SelectorKey 派生后继门控（then 激活 / else 抑制 / route 命中）。
        foreach (var branch in spec.Branches ?? Array.Empty<CircuitBranchSpec>())
            circuit.Append(new BranchSymbol
            {
                ConditionNodeId = branch.NodeId,
                JudgeKey = branch.JudgeKey,
                ThenNodeId = branch.ThenNodeId,
                ElseNodeId = branch.ElseNodeId,
            });

        foreach (var sw in spec.Switches ?? Array.Empty<CircuitSwitchSpec>())
            circuit.Append(new SwitchSymbol
            {
                SelectorNodeId = sw.NodeId,
                SelectorKey = sw.SelectorKey,
                Routes = sw.Routes,
                DefaultNodeId = sw.DefaultNodeId,
            });

        // R2 调用声明 → CallSymbol：调用边在电路层可见；执行面 = Execution.SubCircuitNode（节点 Kind 由声明面的步骤给出）。
        foreach (var call in spec.Calls ?? Array.Empty<CircuitCallSpec>())
            circuit.Append(new CallSymbol { CallerNodeId = call.CallerNodeId, Target = call.Target });

        return circuit;
    }

    /// <summary>步骤 → 电路节点（具名端口）。共享给其它投影器（如调用链投影），保证装配只有一份实现。</summary>
    public static void AppendSteps(Ckt circuit, IReadOnlyList<CircuitStepSpec> steps)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(steps);

        foreach (var s in steps)
        {
            var node = circuit.AddNode(s.Id, s.StepKind);
            foreach (var inp in s.InPorts) node.AddIn(inp);
            foreach (var outp in s.OutPorts) node.AddOut(outp);
        }
    }

    /// <summary>产物→依赖 传递边 → Wire（OutPort → InPort）。</summary>
    public static void AppendFlows(Ckt circuit, IReadOnlyList<CircuitFlowSpec> flows)
    {
        ArgumentNullException.ThrowIfNull(circuit);
        ArgumentNullException.ThrowIfNull(flows);

        foreach (var f in flows)
        {
            circuit.Wire(f.FromNode, f.FromPort, f.ToNode, f.ToPort);
            // 受控边（G3）：判定键随边落进电路（编译期 CFG-025 判可解析、执行期门控）
            if (!string.IsNullOrWhiteSpace(f.When))
                circuit.Wires[^1].When = f.When;
        }
    }
}
