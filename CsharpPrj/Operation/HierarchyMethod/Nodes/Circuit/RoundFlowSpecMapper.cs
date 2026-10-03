using System;
using System.Collections.Generic;
using System.Linq;
using Operation.HierarchyMethod.Nodes.Workflows;
using Share.CircuitFramework.Projection;
using Share.CircuitFramework.Symbols;

namespace Operation.HierarchyMethod.Nodes.Circuit
{
    /// <summary>
    /// 轮骨架 → 电路配方（RoundSpec → CircuitRecipeSpec）：步骤 = 节点（Id = EffectiveNodeId，
    /// 节点实现键，保证同名函数多次出现可解析到具体实例），ResultKeys = 出端口、ParameterKeys = 入端口。
    /// <para>
    /// **StepKind 解析**（Gap 1）：默认 = <see cref="RoundStep.EffectiveNodeId"/>（NodeId 优先，回退 FuncName），
    /// 与既有键名派生口径一致；开启 <paramref name="useFuncNameForKind"/>（区域电路声明面加载路径）时，
    /// StepKind = <see cref="RoundStep.FuncName"/>（完整实现键，含命名空间前缀，如 <c>agent.run/RouteRound</c>）——
    /// 对齐解析器（<see cref="Execution.CircuitNodeResolver"/>)注册的阶段 Kind。区域承载节点步骤再被区域声明
    /// 覆盖为区域 Kind（<c>"{NodeId}#region"</c>），与手建区域节点解析键一致。</para>
    /// <para>区域流（<c>Regions</c> 非空）时做**承载壳收口**：父入口并入承载节点入端口、
    /// 父出口并入承载节点出端口（<c>"NodeId.Port"</c>）、子计划出口并入各区域出映射子端口 ——
    /// 复刻手建区域配方的父/子端口契约（判据载体扇入扇出两端对齐，CFG-020/CFG-023）。</para>
    /// 接线规则（**边声明化**）：<see cref="RoundStep.DependsOn"/> 有声明 ⇒ 以**声明**为准 ——
    /// Wire 只在「声明的前驱 → 后继」之间按端口交集（前驱 ResultKeys ∩ 后继 ParameterKeys）生成；
    /// 无共同数据键的声明边 = **控制边**（仅约束先后），投影为 Sequence 镜像（不产 Wire）；
    /// 未声明的真数据依赖（消费键由更早步骤产出）⇒ 编译期判红（禁隐式调用边）。
    /// <para>
    /// 未声明依赖（全 null）⇒ 回退既有键名派生（链序内「前驱产出 → 后继消费」，同节点自产自消不接线），
    /// 旧模板/内置骨架行为逐字不变。流内无人产出的消费键 = EntryPorts；决策出口 = 产出 decision 的步骤的 decision 端口。
    /// </para>
    /// </summary>
    public static class RoundFlowSpecMapper
    {
        public static CircuitRecipeSpec MapToRecipe(
            RoundSpec spec, bool enforceDeclaredInputs = false, bool useFuncNameForKind = false)
        {
            ArgumentNullException.ThrowIfNull(spec);
            if (spec.Steps is not { Count: > 0 })
                throw new ArgumentException("RoundSpec.Steps 不能为空", nameof(spec));

            var steps = new List<CircuitStepSpec>(spec.Steps.Count);
            var producers = new Dictionary<string, (string StepId, int Index)>(StringComparer.Ordinal);
            for (var i = 0; i < spec.Steps.Count; i++)
            {
                var s = spec.Steps[i];
                var nodeId = s.EffectiveNodeId;
                // Gap 1：区域电路声明面加载路径用 FuncName（完整实现键）为 StepKind；其余保持既有 EffectiveNodeId 口径。
                var nodeKind = useFuncNameForKind && !string.IsNullOrWhiteSpace(s.FuncName) ? s.FuncName : nodeId;
                steps.Add(new CircuitStepSpec(nodeId, nodeKind, s.ParameterKeys, s.ResultKeys));
                foreach (var rk in s.ResultKeys)
                    producers.TryAdd(rk, (nodeId, i));
            }

            var duplicates = steps.GroupBy(s => s.Id, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
            if (duplicates is not null)
                throw new InvalidOperationException(
                    $"[RoundFlow] 工作流 '{spec.Name}' 存在重复节点 Id '{duplicates.Key}'（EffectiveNodeId 必须唯一）。");

            var stepsById = new Dictionary<string, RoundStep>(StringComparer.Ordinal);
            var indexById = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < spec.Steps.Count; i++)
            {
                stepsById[spec.Steps[i].EffectiveNodeId] = spec.Steps[i];
                indexById[spec.Steps[i].EffectiveNodeId] = i;
            }

            var flows = BuildFlows(spec, stepsById, indexById, producers, out var controlEdges);
            var entries = BuildEntries(spec, producers);
            EnforceDeclaredInputContract(spec, entries, enforceDeclaredInputs);
            var exits = BuildExits(spec);

            // R3 控制流（加法式）：中立声明 → 电路配方控制流槽位（分支 / 分派 / 调用）；缺省空 = 源兼容。
            var branches = spec.Branches is { Count: > 0 }
                ? spec.Branches.Select(b => new CircuitBranchSpec(b.NodeId, b.JudgeKey, b.ThenNodeId, b.ElseNodeId)).ToList()
                : null;
            var switches = spec.Switches is { Count: > 0 }
                ? spec.Switches.Select(s => new CircuitSwitchSpec(s.NodeId, s.SelectorKey, s.Routes, s.DefaultNodeId)).ToList()
                : null;
            var calls = spec.Calls is { Count: > 0 }
                ? spec.Calls.Select(c => MapCall(c, useFuncNameForKind)).ToList()
                : null;
            var regions = spec.Regions is { Count: > 0 }
                ? spec.Regions.Select(r => MapRegion(r, useFuncNameForKind)).ToList()
                : null;
            var guards = spec.Guards is { Count: > 0 }
                ? spec.Guards.Select(MapGuard).ToList()
                : null;

            // 受控边（[When]，G3）：判定键判真的 From→To 边查 Flows 命中则内嵌 When，未命中则补受控边。
            var effectiveFlows = ApplyWhens(spec, flows);

            // 区域承载壳收口（Gap 1）：区域节点步骤 Kind = 区域 Kind；父入/出端口并入入口/出口 ——
            // 复刻手建区域配方（父单节点 run/send）的端口契约（扇入扇出两端对齐，CFG-020/CFG-023）。
            ReconcileRegionShell(spec, steps, regions, entries, exits);

            return new CircuitRecipeSpec(spec.Name, entries, exits, steps, effectiveFlows,
                ControlEdges: controlEdges, Branches: branches, Switches: switches, Calls: calls,
                Regions: regions, Guards: guards);
        }

        /// <summary>
        /// 区域承载壳收口：把「区域 = 普通节点」的父侧契约补全（声明面只表达步骤/边/子计划，壳契约由此收口）——
        /// ① 承载区域节点的步骤 <see cref="CircuitStepSpec.StepKind"/> = 区域 Kind（解析键，<c>"{NodeId}#region"</c>）；
        /// ② 父入口并入承载节点入端口（区域节点输入 = 父流外部输入）；
        /// ③ 父出口并入承载节点出端口（<c>"NodeId.Port"</c>，区域节点输出 = 父流最终产物）。
        /// </summary>
        private static void ReconcileRegionShell(
            RoundSpec spec,
            List<CircuitStepSpec> steps,
            IReadOnlyList<CircuitRegionSpec>? regions,
            List<string> entries,
            List<string> exits)
        {
            if (regions is not { Count: > 0 }) return;

            // 原 spec 步骤的端口（避免依赖已被改造的 steps 集合）。
            var inPortsByNode = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            var outPortsByNode = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
            foreach (var s in spec.Steps)
            {
                var id = s.EffectiveNodeId;
                inPortsByNode[id] = s.ParameterKeys;
                outPortsByNode[id] = s.ResultKeys;
            }

            var regionKindByNode = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var r in regions)
                regionKindByNode[r.NodeId] = r.RegionKind;

            // ① 承载节点步骤 Kind → 区域 Kind。
            for (var i = 0; i < steps.Count; i++)
            {
                var st = steps[i];
                if (!regionKindByNode.TryGetValue(st.Id, out var regionKind)) continue;
                steps[i] = st with { StepKind = regionKind };
            }

            // ②③ 父入/出端口并入入口/出口（复用原 spec 端口；去重保序）。
            foreach (var carrierId in regionKindByNode.Keys)
            {
                if (inPortsByNode.TryGetValue(carrierId, out var ins))
                    foreach (var p in ins)
                        if (!entries.Contains(p, StringComparer.Ordinal))
                            entries.Add(p);
                if (outPortsByNode.TryGetValue(carrierId, out var outs))
                    foreach (var p in outs)
                    {
                        var exitName = $"{carrierId}.{p}";
                        if (!exits.Contains(exitName, StringComparer.Ordinal))
                            exits.Add(exitName);
                    }
            }
        }

        /// <summary>区域声明映射（R3+）：子骨架就地递归映射为子电路配方 + 迭代计划（区域 = 封在父层普通节点内的子电路）。</summary>
        private static CircuitRegionSpec MapRegion(RoundRegion region, bool useFuncNameForKind)
        {
            ArgumentNullException.ThrowIfNull(region);
            ArgumentNullException.ThrowIfNull(region.Child);
            var child = MapToRecipe(region.Child, useFuncNameForKind: useFuncNameForKind);
            // CFG-023：子计划出口须在中声明的 out 映射子端口、子入口须含 in 映射子端口（判据两端对齐）。
            var outChildPorts = (region.OutBindings ?? Array.Empty<RoundPortBinding>())
                .Select(b => b.ChildPort).ToList();
            var inChildPorts = (region.InBindings ?? Array.Empty<RoundPortBinding>())
                .Select(b => b.ChildPort).ToList();
            if (outChildPorts.Count > 0 || inChildPorts.Count > 0)
            {
                child = child with
                {
                    ExitPorts = child.ExitPorts.Concat(outChildPorts.Where(p => !child.ExitPorts.Contains(p))).ToList(),
                    EntryPorts = child.EntryPorts.Concat(inChildPorts.Where(p => !child.EntryPorts.Contains(p))).ToList(),
                };
            }
            var plan = region.Iteration switch
            {
                RoundRegionIteration.WhileTrue => RegionIterationPlan.WhileTrue(region.JudgePort!, RegionRequiredMax(region)),
                RoundRegionIteration.UntilTrue => RegionIterationPlan.UntilTrue(region.JudgePort!, RegionRequiredMax(region)),
                _ => RegionIterationPlan.Once(),
            };
            return new CircuitRegionSpec(
                region.NodeId,
                region.NodeId + CircuitRegionSpec.KindSuffix,
                new CalleeRef(child.CircuitId),
                child,
                region.InBindings?.Select(b => new RegionPortBinding(b.ParentPort, b.ChildPort)).ToList(),
                region.OutBindings?.Select(b => new RegionPortBinding(b.ParentPort, b.ChildPort)).ToList(),
                plan);
        }

        private static int RegionRequiredMax(RoundRegion region)
        {
            if (region.MaxIterations is > 0) return region.MaxIterations.Value;
            throw new InvalidOperationException(
                $"[RoundFlow] 区域 '{region.NodeId}' 条件迭代（WhileTrue/UntilTrue）须声明迭代上限 max=&gt;0（R1 有界量化）。");
        }

        /// <summary>调用声明映射（R3）：子骨架就地递归映射为子电路配方（调用 = 一次子电路调用）。</summary>
        private static CircuitCallSpec MapCall(RoundCall call, bool useFuncNameForKind)
        {
            ArgumentNullException.ThrowIfNull(call);
            ArgumentNullException.ThrowIfNull(call.Child);
            return new CircuitCallSpec(
                call.CallerNodeId,
                call.EffectiveCalleeKind,
                new CalleeRef(call.Child.Name),
                MapToRecipe(call.Child, useFuncNameForKind: useFuncNameForKind),
                call.InBindings?.Select(b => new RegionPortBinding(b.ParentPort, b.ChildPort)).ToList(),
                call.OutBindings?.Select(b => new RegionPortBinding(b.ParentPort, b.ChildPort)).ToList());
        }

        /// <summary>受控边应用（R3+，<c>[When]</c>，G3）：命中已有 From→To Wire 则内嵌 When 判定；
        /// 未命中（无数据 Wire）则追加一条带 When 的受控边（受控 = 判定为真才激活，仍经数据键）。</summary>
        private static List<CircuitFlowSpec> ApplyWhens(RoundSpec spec, List<CircuitFlowSpec> flows)
        {
            if (spec.Whens is not { Count: > 0 }) return flows;
            var result = flows.ToList();
            foreach (var w in spec.Whens)
            {
                var existing = result.FindIndex(f =>
                    string.Equals(f.FromNode, w.From, StringComparison.Ordinal)
                    && string.Equals(f.ToNode, w.To, StringComparison.Ordinal));
                if (existing >= 0)
                {
                    var f = result[existing];
                    result[existing] = f with { When = w.JudgeKey };
                }
                else
                {
                    // 无数据边的受控依赖：经首公共键门控，否则无口径 → 编译期由 Gate 判红可解析性。
                    result.Add(new CircuitFlowSpec(w.From, w.JudgeKey, w.To, w.JudgeKey, When: w.JudgeKey));
                }
            }
            return result;
        }

        /// <summary>受护作用域映射（R3+，<c>[Guard]</c>，G4）：作用域内节点失败 → 吞并继续。</summary>
        private static CircuitGuardSpec MapGuard(RoundGuardDecl guard)
            => new(guard.ProtectedNodeIds);

        /// <summary>
        /// 边集合构造：声明模式（任一步骤声明 <see cref="RoundStep.DependsOn"/>）以声明为准，否则回退键名派生。
        /// 控制边（声明的前驱与后继无共同数据键）计入 <paramref name="controlEdges"/>（不产 Wire）。
        /// </summary>
        private static List<CircuitFlowSpec> BuildFlows(
            RoundSpec spec,
            IReadOnlyDictionary<string, RoundStep> stepsById,
            IReadOnlyDictionary<string, int> indexById,
            IReadOnlyDictionary<string, (string StepId, int Index)> producers,
            out List<CircuitControlEdgeSpec> controlEdges)
        {
            var flows = new List<CircuitFlowSpec>();
            controlEdges = new List<CircuitControlEdgeSpec>();
            if (spec.Steps.Any(s => s.DependsOn is { Count: > 0 }))
            {
                BuildDeclaredFlows(spec, stepsById, indexById, controlEdges, flows);
                EnforceDeclaredEdgeCoverage(spec, producers);
                return flows;
            }

            BuildDerivedFlows(spec, producers, flows);
            return flows;
        }

        /// <summary>键名派生（未声明依赖时的既有行为）：链序内「前驱产出 → 后继消费」；同节点自产自消不接线。</summary>
        private static void BuildDerivedFlows(
            RoundSpec spec, IReadOnlyDictionary<string, (string StepId, int Index)> producers, List<CircuitFlowSpec> flows)
        {
            for (var ti = 0; ti < spec.Steps.Count; ti++)
            {
                var t = spec.Steps[ti];
                var tNode = t.EffectiveNodeId;
                foreach (var p in t.ParameterKeys)
                {
                    if (!producers.TryGetValue(p, out var prod) || prod.StepId == tNode || prod.Index >= ti) continue;
                    flows.Add(new CircuitFlowSpec(prod.StepId, p, tNode, p));
                }
            }
        }

        /// <summary>
        /// 声明模式：逐声明边生成 Wire（端口对 = 前驱 ResultKeys ∩ 后继 ParameterKeys）；
        /// 无交集 = 控制边（值面走 ctx/KeyMapping，图以 Sequence 镜像可见）。
        /// 判据（编译期）：声明边端点须为流内步骤键 ∧ 前驱须早于后继（声明序是声明边 DAG 的线性扩展）。
        /// </summary>
        private static void BuildDeclaredFlows(
            RoundSpec spec,
            IReadOnlyDictionary<string, RoundStep> stepsById,
            IReadOnlyDictionary<string, int> indexById,
            List<CircuitControlEdgeSpec> controlEdges,
            List<CircuitFlowSpec> flows)
        {
            for (var ti = 0; ti < spec.Steps.Count; ti++)
            {
                var t = spec.Steps[ti];
                var tNode = t.EffectiveNodeId;
                foreach (var raw in t.DependsOn ?? Array.Empty<string>())
                {
                    var pred = (raw ?? string.Empty).Trim();
                    if (!indexById.TryGetValue(pred, out var pi))
                        throw new InvalidOperationException(
                            $"[RoundFlow] 工作流 '{spec.Name}' 步骤 '{tNode}' 声明的依赖 '{pred}' 在流内不存在（声明边端点须为步骤键）。");
                    if (pi >= ti)
                        throw new InvalidOperationException(
                            $"[RoundFlow] 工作流 '{spec.Name}' 声明边 '{pred} → {tNode}' 与步骤序矛盾（前驱须早于后继）。");

                    var keys = t.ParameterKeys
                        .Where(k => stepsById[pred].ResultKeys.Contains(k, StringComparer.Ordinal))
                        .ToList();
                    if (keys.Count == 0)
                    {
                        controlEdges.Add(new CircuitControlEdgeSpec(pred, tNode));
                        continue;
                    }

                    foreach (var k in keys)
                        flows.Add(new CircuitFlowSpec(pred, k, tNode, k));
                }
            }
        }

        /// <summary>
        /// 声明覆盖判据（仅声明模式）：真数据依赖（消费键由更早步骤产出）必须已声明 ——
        /// 否则「图上没这条边、值面却在用」= 隐式调用边（按函数名写死的回灌/解析即此形态）。
        /// </summary>
        private static void EnforceDeclaredEdgeCoverage(
            RoundSpec spec, IReadOnlyDictionary<string, (string StepId, int Index)> producers)
        {
            for (var ti = 0; ti < spec.Steps.Count; ti++)
            {
                var t = spec.Steps[ti];
                var tNode = t.EffectiveNodeId;
                var declared = (t.DependsOn ?? Array.Empty<string>())
                    .Select(p => (p ?? string.Empty).Trim())
                    .ToHashSet(StringComparer.Ordinal);

                foreach (var key in t.ParameterKeys)
                {
                    if (!producers.TryGetValue(key, out var prod)) continue;
                    if (prod.StepId == tNode || prod.Index >= ti) continue;
                    if (declared.Contains(prod.StepId)) continue;
                    throw new InvalidOperationException(
                        $"[RoundFlow] 工作流 '{spec.Name}' 步骤 '{tNode}' 消费键 '{key}'（由更早步骤 '{prod.StepId}' 产出）"
                        + "但未声明依赖边：谁调谁须由声明面给出（禁隐式调用边）。");
                }
            }
        }

        private static List<string> BuildEntries(
            RoundSpec spec, IReadOnlyDictionary<string, (string StepId, int Index)> producers)
        {
            var consumed = spec.Steps.SelectMany(s => s.ParameterKeys).Distinct().ToHashSet(StringComparer.Ordinal);
            var produced = producers.Keys.ToHashSet(StringComparer.Ordinal);
            return consumed.Except(produced).OrderBy(k => k, StringComparer.Ordinal).ToList();
        }

        private static void EnforceDeclaredInputContract(
            RoundSpec spec, IReadOnlyList<string> entries, bool enforce)
        {
            if (!enforce || spec.InputPorts is not { Count: > 0 }) return;
            var declared = spec.InputPorts.Select(p => p.Key).ToHashSet(StringComparer.Ordinal);
            var missing = entries.Where(k => !declared.Contains(k)).ToList();
            if (missing.Count > 0)
                throw new InvalidOperationException(
                    $"[RoundFlow] 工作流 '{spec.Name}' 消费未声明键（流内无人产出、InputPorts 未声明）："
                    + string.Join("；", missing));
        }

        private static List<string> BuildExits(RoundSpec spec)
        {
            var exits = new List<string>();
            if (spec.SuccessCriterion == RoundSpecSuccessCriterion.DecisionWord)
            {
                var decisionStep = spec.Steps.LastOrDefault(s => s.ResultKeys.Contains("decision"));
                if (decisionStep is not null) exits.Add($"{decisionStep.EffectiveNodeId}.decision");
            }
            return exits;
        }
    }
}
