using System;
using System.Collections.Generic;
using System.Linq;
using DataCenter.Protocol.Standards;
using Share.CircuitFramework.Projection;
using Strategy.Observability.Ports;

namespace Operation.HierarchyMethod.Nodes.Circuit
{
    /// <summary>
    /// 端口电路 → 求值 IR（CF-3）：「Program 由 <see cref="CircuitRecipeSpec"/> 投影生成」的实现。
    /// <para>
    /// 分工（口径 B）：**结构 / 顺序取自 recipe**（<see cref="PortCircuitProjector"/> 产出的步骤序 = 声明序），
    /// **操作数载荷取自标准声明**（<c>PortStandard</c>；recipe 不承载领域载荷 ⇒ 框架保持零领域类型）。
    /// </para>
    /// <para>
    /// 目的 = 使 <c>PortEvaluator</c> 执行的 IR 与电路拓扑**结构同源**（同一 recipe 定序），
    /// 与 <see cref="Strategy.Observability.Ports.PortSpecCompiler"/> 的手写 IR **逐项等价**（对拍判据见测试）。
    /// </para>
    /// </summary>
    public static class PortCircuitIrProjector
    {
        /// <summary>由端口声明投影 IR（内部经唯一编译入口无关——本件只做结构映射，不执行、不编译）。</summary>
        public static IReadOnlyList<PortInstr> Project(PortStandard port)
        {
            ArgumentNullException.ThrowIfNull(port);
            return Project(port, PortCircuitProjector.MapToRecipe(port));
        }

        /// <summary>由「端口声明 + 已投影配方」生成 IR（配方定序，标准定载荷）。</summary>
        public static IReadOnlyList<PortInstr> Project(PortStandard port, CircuitRecipeSpec recipe)
        {
            ArgumentNullException.ThrowIfNull(port);
            ArgumentNullException.ThrowIfNull(recipe);

            var impl = port.Impl;
            if (impl is null) return Array.Empty<PortInstr>();

            if (string.Equals(impl.Kind, PortImplVocabulary.KindName.Reflective, StringComparison.Ordinal))
                return Reflective(recipe, impl);

            if (string.Equals(impl.Kind, PortImplVocabulary.KindName.Composed, StringComparison.Ordinal))
                return Composed(recipe, impl);

            if (string.Equals(impl.Kind, PortImplVocabulary.KindName.Scripted, StringComparison.Ordinal))
                return Scripted(recipe, impl);

            return Array.Empty<PortInstr>();
        }

        /// <summary>反射族：单节点 ⇒ 单条 <c>Call</c>（目标不全 ⇒ 空程序，与手写 IR 同口径）。</summary>
        private static IReadOnlyList<PortInstr> Reflective(CircuitRecipeSpec recipe, PortImplStandard impl)
        {
            var singleReflective = recipe.Steps.Count == 1
                && string.Equals(recipe.Steps[0].StepKind, PortCircuitProjector.ReflectiveStepKind, StringComparison.Ordinal);
            if (!singleReflective) return Array.Empty<PortInstr>();

            var complete = !string.IsNullOrWhiteSpace(impl.Target) && !string.IsNullOrWhiteSpace(impl.Method);
            return complete
                ? new[]
                {
                    new PortInstr(
                        PortOp.Call,
                        Target: impl.Target,
                        Method: impl.Method,
                        Args: impl.Args ?? Array.Empty<PortArgStandard>()),
                }
                : Array.Empty<PortInstr>();
        }

        /// <summary>
        /// 组合族：<c>[Dep…, Call…, Project…]</c>。Dep/Call 的**出现序取自 recipe 步骤序**；Project 载荷
        /// （recipe 里被折进调用节点出口，无独立步骤）按标准声明序补齐 —— 与手写 IR 逐位对齐。
        /// </summary>
        private static IReadOnlyList<PortInstr> Composed(CircuitRecipeSpec recipe, PortImplStandard impl)
        {
            var deps = impl.Deps ?? Array.Empty<DepStandard>();
            var calls = impl.Calls ?? Array.Empty<CallStandard>();
            var project = impl.Project ?? Array.Empty<ComposedFieldStandard>();

            // 终止判据（与 PortSpecCompiler.Composed 同口径：无调用 / 重复引用名 / 无投影 ⇒ 空程序）
            var names = new HashSet<string>(StringComparer.Ordinal);
            var duplicate = calls.FirstOrDefault(call => !names.Add(call.As));
            if (calls.Count == 0 || duplicate is not null || project.Count == 0)
                return Array.Empty<PortInstr>();

            var program = new List<PortInstr>(deps.Count + calls.Count + project.Count);
            var byRef = calls.ToDictionary(call => call.As, StringComparer.Ordinal);
            var depCursor = 0;

            foreach (var step in recipe.Steps)
            {
                if (string.Equals(step.StepKind, PortCircuitProjector.DepStepKind, StringComparison.Ordinal))
                {
                    if (depCursor < deps.Count) program.Add(new PortInstr(PortOp.Dep, Operand: deps[depCursor]));
                    depCursor++;
                }
                else if (string.Equals(step.StepKind, PortCircuitProjector.CallStepKind, StringComparison.Ordinal)
                         && step.OutPorts.Count > 0 && byRef.TryGetValue(step.OutPorts[0], out var call))
                {
                    program.Add(new PortInstr(
                        PortOp.Call, Out: call.As, Target: call.Target, Method: call.Method, Dep: call.Dep, Operand: call));
                }
            }

            foreach (var field in project)
                program.Add(new PortInstr(PortOp.Project, Operand: field));

            return program;
        }

        /// <summary>
        /// 脚本族：<c>Program = Steps(script)</c>（步骤映射单源 = <see cref="Strategy.Observability.Ports.PortSpecCompiler.Steps"/>，
        /// 不另造同形副本）；无行为声明 ⇒ 空程序（与手写 IR 同口径）。
        /// </summary>
        private static IReadOnlyList<PortInstr> Scripted(CircuitRecipeSpec recipe, PortImplStandard impl)
        {
            var deliver = impl.Deliver ?? new DeliverStandard();
            var program = Strategy.Observability.Ports.PortSpecCompiler.Steps(impl.Script ?? Array.Empty<StepStandard>());

            var behaves = program.Count > 0 || (impl.ScriptProject?.Count ?? 0) > 0 || deliver.Capture is not null
                || deliver.DeferSteps is { Count: > 0 } || deliver.Value is not null || impl.Scenarios is not null;

            return behaves ? program : Array.Empty<PortInstr>();
        }
    }
}
