using System;
using System.Collections.Generic;
using System.Linq;

namespace Operation.HierarchyMethod.Nodes.Workflows
{
    /// <summary>
    /// 声明流按需投影（F4）：给定期望产物端口，按「产出端口 → 生产步骤」反向切片，
    /// 只保留达成这些产物所需的最小步骤集（原序）。未命中的产物列入 <see cref="MissingOutputs"/>。
    /// <para>纯函数、零执行：消费方（OneShotFlowRunner）据结果缩减 RoundSpec.Steps，
    /// 未选中步骤不会被执行 ⇒ 无副作用。</para>
    /// </summary>
    public sealed record RoundSpecProjection(
        RoundSpec Spec,
        IReadOnlyList<string> ExecutedSteps,
        IReadOnlyList<string> MissingOutputs,
        int TotalStepCount)
    {
        /// <summary>所有期望产物均有生产步骤。</summary>
        public bool Ok => MissingOutputs.Count == 0;

        /// <summary>是否发生裁剪（未裁剪 = 全量步骤）。</summary>
        public bool Trimmed => ExecutedSteps.Count != TotalStepCount;
    }

    /// <summary>RoundSpec 依赖切片（唯一实现；禁各调用点手写「只跑枚举」开关）。</summary>
    public static class RoundSpecProjector
    {
        /// <summary>按期望产物反向裁剪步骤集（保持声明序）。</summary>
        public static RoundSpecProjection Project(
            RoundSpec spec,
            IReadOnlyCollection<string> requiredOutputs)
        {
            ArgumentNullException.ThrowIfNull(spec);
            ArgumentNullException.ThrowIfNull(requiredOutputs);

            var required = requiredOutputs
                .Where(static p => !string.IsNullOrWhiteSpace(p))
                .Select(static p => p.Trim())
                .Distinct(StringComparer.Ordinal)
                .ToList();
            if (required.Count == 0)
                return new RoundSpecProjection(
                    spec,
                    spec.Steps.Select(static s => s.EffectiveNodeId).ToList(),
                    Array.Empty<string>(),
                    spec.Steps.Count);

            // 端口 → 生产步骤（同端口多次产出时取最后一步 = 数据空间最终值）。
            var producers = new Dictionary<string, int>(StringComparer.Ordinal);
            for (var i = 0; i < spec.Steps.Count; i++)
            {
                foreach (var port in spec.Steps[i].ResultKeys)
                {
                    if (!string.IsNullOrWhiteSpace(port)) producers[port] = i;
                }
            }

            var selected = new HashSet<int>();
            var needed = new Queue<string>(required);
            var seen = new HashSet<string>(StringComparer.Ordinal);
            while (needed.Count > 0)
            {
                var port = needed.Dequeue();
                if (!seen.Add(port)) continue;
                if (!producers.TryGetValue(port, out var index)) continue;
                if (!selected.Add(index)) continue;
                foreach (var input in spec.Steps[index].ParameterKeys)
                {
                    if (!string.IsNullOrWhiteSpace(input)) needed.Enqueue(input);
                }
            }

            var missing = required.Where(p => !producers.ContainsKey(p)).ToList();
            var steps = selected.OrderBy(static i => i).Select(i => spec.Steps[i]).ToList();
            var executed = steps.Select(static s => s.EffectiveNodeId).ToList();
            return new RoundSpecProjection(spec with { Steps = steps }, executed, missing, spec.Steps.Count);
        }
    }
}
