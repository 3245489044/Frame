using System;
using System.Collections.Concurrent;
using System.Linq;
using System.Text;
using Operation.HierarchyMethod.Nodes.Workflows;
using Share.CircuitFramework.Compiler;
using Share.CircuitFramework.Core;
using Share.CircuitFramework.Projection;
using Ckt = Share.CircuitFramework.Core.Circuit;

namespace Operation.HierarchyMethod.Nodes.Circuit
{
    /// <summary>
    /// 轮骨架电路进程级缓存（每进程每工作流一次构建）。
    /// <para>
    /// 编译经框架**单一入口** <see cref="CircuitCompiler.TryCompile(CircuitRecipeSpec, out CompiledRecipe, out IReadOnlyList{CircuitDiagnostic})"/>：
    /// 旧实现自行重排「产物图校验 → 投影 → 编译」三步，与框架 <c>CircuitAssemblyRunner</c> 单点并存 = 漂移面（已删）。
    /// </para>
    /// <para>
    /// 缓存键 = **内容指纹**（投影输入全集：流名 / 判据 / InputPorts / 有序步骤端口集），而非 <c>spec.Name</c>：
    /// 同名不同内容（.ushar 热改、模板派生、多宿主同名流）必须重建，否则会执行陈旧拓扑。
    /// 指纹不含 <c>GlobalKeyMapping</c>（不参与拓扑投影，仅执行期消费）。
    /// </para>
    /// <para>
    /// 构建失败（产物图 / 环 / 拓扑违例）上抛 <see cref="InvalidOperationException"/>（Agent 侧既有契约），
    /// 由后端按门控（LogOnly 回退 / FailFast 上抛）处置；失败不写缓存（下次请求重试）。
    /// 构建体无共享可变状态，故无需全局锁（同名并发首建允许重复一次，结果等值）。
    /// </para>
    /// </summary>
    public static class RoundFlowCircuitRegistry
    {
        public sealed record BuiltCircuit(CircuitRecipeSpec Spec, Ckt Circuit, ExecutionPlan Plan);

        private static readonly ConcurrentDictionary<string, BuiltCircuit> Cache = new(StringComparer.Ordinal);

        /// <summary>键分隔符（指纹前缀 = 流名，供 <see cref="Invalidate"/> 按名前缀失效）。</summary>
        private const char KeySeparator = '\u001F';

        /// <summary>已缓存条数（诊断 / 门禁正对照）。</summary>
        public static int Count => Cache.Count;

        /// <summary>取（或首次构建）该工作流的电路编译产物；同一指纹只构建一次。</summary>
        public static BuiltCircuit GetOrBuild(RoundSpec spec, bool enforceDeclaredInputs = false)
        {
            ArgumentNullException.ThrowIfNull(spec);
            return Cache.GetOrAdd(Fingerprint(spec, enforceDeclaredInputs), _ => Build(spec, enforceDeclaredInputs));
        }

        /// <summary>按工作流名失效（声明面热改后调用；无匹配 = no-op）。</summary>
        public static void Invalidate(string specName)
        {
            var prefix = (specName ?? string.Empty) + KeySeparator;
            foreach (var key in Cache.Keys.Where(k => k.StartsWith(prefix, StringComparison.Ordinal)).ToList())
                Cache.TryRemove(key, out _);
        }

        /// <summary>清空缓存（门禁 / 测试用）。</summary>
        public static void Clear() => Cache.Clear();

        /// <summary>
        /// 内容指纹（投影输入全集；顺序敏感）：覆盖 <see cref="RoundFlowSpecMapper.MapToRecipe"/> 的全部入参来源
        /// （流名 / 判据 / InputPorts / 步骤有序 Id 与端口集 / **声明依赖边**），使「内容变 ⇒ 键变 ⇒ 重建」。
        /// </summary>
        internal static string Fingerprint(RoundSpec spec, bool enforceDeclaredInputs)
        {
            var sb = new StringBuilder(256);
            sb.Append(spec.Name ?? string.Empty).Append(KeySeparator)
              .Append(enforceDeclaredInputs ? '1' : '0').Append(KeySeparator)
              .Append(spec.SuccessCriterion).Append(KeySeparator);

            if (spec.InputPorts is { Count: > 0 })
                foreach (var port in spec.InputPorts) sb.Append(port.Key).Append(',');

            sb.Append(KeySeparator);

            if (spec.Steps is { Count: > 0 })
                foreach (var step in spec.Steps)
                {
                    sb.Append(step.EffectiveNodeId).Append(':');
                    foreach (var key in step.ParameterKeys) sb.Append(key).Append(',');
                    sb.Append(':');
                    foreach (var key in step.ResultKeys) sb.Append(key).Append(',');
                    // 依赖边声明参与投影（边集/控制边由它决定）⇒ 必须进指纹，否则同名同内容键复用陈旧电路
                    sb.Append(':');
                    foreach (var pred in step.DependsOn ?? Array.Empty<string>()) sb.Append(pred).Append(',');
                    sb.Append(';');
                }

            return sb.ToString();
        }

        private static BuiltCircuit Build(RoundSpec spec, bool enforceDeclaredInputs)
        {
            // 唯一编译入口（框架单点：配方校验 → 投影 → 编译），消费方不重排步骤序
            var recipe = RoundFlowSpecMapper.MapToRecipe(spec, enforceDeclaredInputs);
            if (!CircuitCompiler.TryCompile(recipe, out var compiled, out var diagnostics))
                throw new InvalidOperationException(
                    "[RoundFlow] 轮骨架编译失败（" + spec.Name + "）："
                    + string.Join("；", diagnostics.Select(d => d.Code + " " + d.Message)));
            return new BuiltCircuit(compiled!.Spec, compiled.Circuit, compiled.Plan);
        }
    }
}
