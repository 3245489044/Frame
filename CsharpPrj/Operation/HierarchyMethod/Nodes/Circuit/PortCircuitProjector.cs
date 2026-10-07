using System;
using System.Collections.Generic;
using System.Linq;
using DataCenter.Protocol.Standards;
using Share.CircuitFramework.Compiler;
using Share.CircuitFramework.Projection;

namespace Operation.HierarchyMethod.Nodes.Circuit
{
    /// <summary>
    /// 端口标准 → 电路配方（CF-1 读侧投影）：把 <see cref="PortStandard"/>（含 <see cref="PortImplStandard"/>）
    /// 投影为 <see cref="CircuitRecipeSpec"/>，经框架**唯一编译入口**
    /// <see cref="CircuitCompiler.TryCompile"/> 产出诊断清单。**不接执行**（对既有 <c>PortEvaluator</c> 路径零行为变更）。
    /// <para>
    /// 投影口径（对齐 CF 施工单 §0/§2 与既有 <see cref="RoundFlowSpecMapper"/> 范式）：
    /// 一个端口 = 一张电路；<see cref="CircuitStepSpec.StepKind"/> 为不透明实现键（执行面解析见 CF-3/CF-4）；
    /// 出端口名 = 产物名，出口 <c>ExitPorts</c> 形如 <c>"NodeId.Port"</c>；入口 <c>EntryPorts</c> = 端口声明的输入名。
    /// </para>
    /// <para>
    /// 值面不进电路（<see cref="PortImplStandard.Project"/> / <c>ScriptProject</c> 的取值法只在节点体内求值），
    /// 本投影只承载「结构 + 顺序 + 出口字段名」。
    /// </para>
    /// </summary>
    public static class PortCircuitProjector
    {
        /// <summary>节点实现键：反射族单节点。</summary>
        public const string ReflectiveStepKind = "port.reflective";

        /// <summary>节点实现键：组合族依赖构建节点（黑盒体）。</summary>
        public const string DepStepKind = "port.dep";

        /// <summary>节点实现键：组合族调用节点（黑盒体）。</summary>
        public const string CallStepKind = "port.call";

        /// <summary>节点实现键：脚本族步骤节点（黑盒体）。</summary>
        public const string ScriptedStepKind = "port.step";

        /// <summary>反射族节点 Id（单节点，固定）。</summary>
        private const string ReflectiveNodeId = "call";

        /// <summary>单端口投影 + 编译结果（诊断清单）。</summary>
        public sealed record PortCircuitResult(
            string PortKey,
            CircuitRecipeSpec Recipe,
            bool Compiled,
            IReadOnlyList<CircuitDiagnostic> Diagnostics);

        /// <summary>投影单端口并跑唯一编译入口，返回配方 + 诊断（编译失败不清空配方，供诊断定位）。</summary>
        public static PortCircuitResult Project(PortStandard port)
        {
            ArgumentNullException.ThrowIfNull(port);

            var recipe = MapToRecipe(port);
            var compiled = CircuitCompiler.TryCompile(recipe, out _, out var diagnostics);
            return new PortCircuitResult(port.Key, recipe, compiled, diagnostics);
        }

        /// <summary>批量投影（全量端口台账）；顺序 = 输入声明序。</summary>
        public static IReadOnlyList<PortCircuitResult> ProjectAll(IEnumerable<PortStandard> ports)
        {
            ArgumentNullException.ThrowIfNull(ports);
            return ports.Select(Project).ToList();
        }

        /// <summary>声明 → 电路配方（按 <see cref="PortImplStandard.Kind"/> 择路；未迁移族 → 空配方，交编译期给诊断）。</summary>
        public static CircuitRecipeSpec MapToRecipe(PortStandard port)
        {
            ArgumentNullException.ThrowIfNull(port);

            var impl = port.Impl;
            if (impl is null) return Empty(port);

            if (string.Equals(impl.Kind, PortImplVocabulary.KindName.Reflective, StringComparison.Ordinal))
                return Reflective(port);

            if (string.Equals(impl.Kind, PortImplVocabulary.KindName.Composed, StringComparison.Ordinal))
                return Composed(port, impl);

            if (string.Equals(impl.Kind, PortImplVocabulary.KindName.Scripted, StringComparison.Ordinal))
                return Scripted(port, impl);

            return Empty(port);
        }

        /// <summary>反射族：单节点（目标类型 / 方法为黑盒体参数），产物字段即出口。</summary>
        private static CircuitRecipeSpec Reflective(PortStandard port)
        {
            var inPorts = EntryNames(port);
            var outPorts = port.OutFields.Count > 0 ? port.OutFields.ToList() : new List<string> { "result" };

            var steps = new List<CircuitStepSpec>
            {
                new(ReflectiveNodeId, ReflectiveStepKind, inPorts, outPorts),
            };

            var exits = outPorts.Select(field => $"{ReflectiveNodeId}.{field}").ToList();
            return new CircuitRecipeSpec(port.Key, inPorts, exits, steps, Array.Empty<CircuitFlowSpec>());
        }

        /// <summary>组合族：依赖节点（黑盒体）+ 调用节点（引用名即产物；投影字段并入其出口）。</summary>
        private static CircuitRecipeSpec Composed(PortStandard port, PortImplStandard impl)
        {
            var deps = impl.Deps ?? Array.Empty<DepStandard>();
            var calls = impl.Calls ?? Array.Empty<CallStandard>();
            var project = impl.Project ?? Array.Empty<ComposedFieldStandard>();

            var steps = new List<CircuitStepSpec>();
            var flows = new List<CircuitFlowSpec>();

            for (var index = 0; index < deps.Count; index++)
            {
                var depId = DepNodeId(index);
                steps.Add(new CircuitStepSpec(depId, DepStepKind, Array.Empty<string>(), new[] { depId }));
            }

            var callNodeIds = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (var call in calls)
            {
                var nodeId = CallNodeId(call.As);
                callNodeIds[call.As] = nodeId;

                var inPorts = new List<string>();
                if (call.Dep is int depIndex && depIndex >= 0 && depIndex < deps.Count)
                {
                    var depId = DepNodeId(depIndex);
                    inPorts.Add(depId);
                    flows.Add(new CircuitFlowSpec(depId, depId, nodeId, depId));
                }

                var outPorts = new List<string> { call.As };
                foreach (var field in project)
                    if (string.Equals(field.Call, call.As, StringComparison.Ordinal) && !string.IsNullOrEmpty(field.Field))
                        outPorts.Add(field.Field);

                steps.Add(new CircuitStepSpec(nodeId, CallStepKind, inPorts, outPorts));
            }

            var entries = EntryNames(port);
            AttachEntriesToFirstConsumer(steps, entries, CallStepKind);

            var exits = new List<string>();
            foreach (var field in project)
            {
                if (string.IsNullOrEmpty(field.Field)) continue;

                var nodeId = field.Call is { Length: > 0 } callRef && callNodeIds.TryGetValue(callRef, out var callNodeId)
                    ? callNodeId
                    : field.Dep is int depIndex ? DepNodeId(depIndex) : null;

                if (nodeId is null) continue;

                EnsureOutPort(steps, nodeId, field.Field);
                exits.Add($"{nodeId}.{field.Field}");
            }

            return new CircuitRecipeSpec(port.Key, entries, exits, steps, flows);
        }

        /// <summary>脚本族：每步骤一节点（写槽名即产物）；脚本投影字段并入末节点出口。</summary>
        private static CircuitRecipeSpec Scripted(PortStandard port, PortImplStandard impl)
        {
            var script = impl.Script ?? Array.Empty<StepStandard>();
            var project = impl.ScriptProject ?? Array.Empty<FieldExprStandard>();

            var steps = new List<CircuitStepSpec>();
            for (var index = 0; index < script.Count; index++)
            {
                var slot = SlotOf(script[index]);
                var outPorts = slot is null ? Array.Empty<string>() : new[] { slot };
                steps.Add(new CircuitStepSpec(StepNodeId(index), ScriptedStepKind, Array.Empty<string>(), outPorts));
            }

            var entries = EntryNames(port);
            AttachEntriesToFirstConsumer(steps, entries, ScriptedStepKind);

            var exits = new List<string>();
            if (steps.Count > 0)
            {
                var tailId = steps[^1].Id;
                foreach (var field in project)
                {
                    if (string.IsNullOrEmpty(field.Field)) continue;
                    EnsureOutPort(steps, tailId, field.Field);
                    exits.Add($"{tailId}.{field.Field}");
                }
            }

            return new CircuitRecipeSpec(port.Key, entries, exits, steps, Array.Empty<CircuitFlowSpec>());
        }

        /// <summary>未迁移族（delegate / frozen / 未知）：空配方 —— 由编译期给出诊断（不静默、不伪造节点）。</summary>
        private static CircuitRecipeSpec Empty(PortStandard port)
            => new(port.Key, EntryNames(port), Array.Empty<string>(),
                Array.Empty<CircuitStepSpec>(), Array.Empty<CircuitFlowSpec>());

        /// <summary>声明输入名（入口产物）。</summary>
        private static List<string> EntryNames(PortStandard port)
            => port.Inputs.Select(input => input.Name).ToList();

        private static string DepNodeId(int index) => $"dep_{index}";

        private static string CallNodeId(string reference) => $"call_{reference}";

        private static string StepNodeId(int index) => $"step_{index}";

        /// <summary>脚本步骤写槽名（判别式形态 → 产物名；<c>throw</c> / 无写槽步骤 = null）。</summary>
        private static string? SlotOf(StepStandard step)
            => step.New?.As
               ?? step.Invoke?.As
               ?? step.Foreach?.As
               ?? (step.Source is not null ? step.As : null);

        /// <summary>
        /// 入口消费附着：把声明输入并入首个「指定种类」节点入端口（否则回退首节点），
        /// 使「声明输入被接线」（否则编译期按未接线输入判红 —— 这里只做结构性接线，语义由节点体承担）。
        /// </summary>
        private static void AttachEntriesToFirstConsumer(
            List<CircuitStepSpec> steps, IReadOnlyList<string> entries, string consumerStepKind)
        {
            if (steps.Count == 0 || entries.Count == 0) return;

            var index = steps.FindIndex(step => string.Equals(step.StepKind, consumerStepKind, StringComparison.Ordinal));
            if (index < 0) index = 0;

            var inPorts = steps[index].InPorts.ToList();
            foreach (var entry in entries)
                if (!inPorts.Contains(entry, StringComparer.Ordinal))
                    inPorts.Add(entry);

            steps[index] = steps[index] with { InPorts = inPorts };
        }

        /// <summary>确保节点出端口含指定产物名（投影字段并入承载节点出口）。</summary>
        private static void EnsureOutPort(List<CircuitStepSpec> steps, string nodeId, string port)
        {
            for (var index = 0; index < steps.Count; index++)
            {
                if (!string.Equals(steps[index].Id, nodeId, StringComparison.Ordinal)) continue;
                if (steps[index].OutPorts.Contains(port, StringComparer.Ordinal)) return;

                var outPorts = steps[index].OutPorts.ToList();
                outPorts.Add(port);
                steps[index] = steps[index] with { OutPorts = outPorts };
                return;
            }
        }
    }
}
