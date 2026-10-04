using System;
using System.Collections.Generic;
using System.Linq;
using BasicMethod.Declarative.Graph;
using BasicMethod.Declarative.Spec;

namespace Strategy.DecisionRules
{
    /// <summary>
    /// 坐标系不变量（D3 单一实现）：轴唯一 / 每输入轴恰一词元 / 输出轴与标签已注册 / 规则引用词元存在 / 无死轴。
    /// Agent 侧 <c>DecisionCoordinateSchema</c> 与 Frame 侧 <c>ConfigDrivenDecisionController</c> 均只做
    /// 「取词表与轴集 → 调用本类」，不再各维护一份同构校验（SingleImplementation / TH-12）。
    /// </summary>
    public static class CoordinateInvariants
    {
        /// <summary>输入类（I+E）每轮恰一词元的完备性断言（轴内互斥完备、无空档/多值、词元已注册）。</summary>
        public static void AssertEvidenceComplete(
            SemanticVocabulary vocabulary,
            IEnumerable<string> inputAxes,
            IReadOnlyList<(string Axis, string Term)>? evidence)
        {
            evidence = PortGuard.Required(evidence, nameof(evidence));
            var byAxis = evidence.GroupBy(e => e.Axis).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var axis in inputAxes)
            {
                var count = byAxis.TryGetValue(axis, out var list) ? list.Count : 0;
                if (count != 1)
                    throw new ArgumentException("输入轴 " + axis + " 必须恰有一个词元（轴内互斥完备）；实际 " + count);
                if (vocabulary.IndexOf(new SemanticTerm(axis, byAxis[axis][0].Term)) < 0)
                    throw new ArgumentException("输入轴 " + axis + " 词元未注册：" + byAxis[axis][0].Term);
            }
        }

        /// <summary>
        /// 图与坐标一致性校验：层输出轴须为已注册输出轴；输出标签与规则引用词元须已注册；
        /// 每条输入轴至少被一条规则引用（死轴暴露）。
        /// </summary>
        public static void ValidateGraph(
            SemanticVocabulary vocabulary,
            IEnumerable<string> outputAxes,
            IEnumerable<string> inputAxes,
            DecisionGraph graph)
        {
            graph = PortGuard.Required(graph, nameof(graph));
            var outSet = outputAxes.ToHashSet(StringComparer.Ordinal);
            foreach (var layer in graph.Layers)
            {
                if (!outSet.Contains(layer.OutputAxis))
                    throw new ArgumentException("层 " + layer.Name + " 输出轴 " + layer.OutputAxis + " 未注册为输出轴");
                foreach (var rule in layer.Rules)
                {
                    if (vocabulary.IndexOf(new SemanticTerm(layer.OutputAxis, rule.Label)) < 0)
                        throw new ArgumentException("层 " + layer.Name + " 输出标签 " + rule.Label + " 未在坐标注册");
                    foreach (var req in rule.Required)
                        if (vocabulary.IndexOf(req) < 0)
                            throw new ArgumentException("层 " + layer.Name + " 规则 " + rule.Label + " 引用未注册词元：" + req);
                }
            }

            AssertNoDeadAxes(inputAxes, graph.Layers.SelectMany(l => l.Rules));
        }

        /// <summary>死轴断言：每条输入轴至少被一条规则引用（补规则或从坐标注册表移除，二选一）。</summary>
        public static void AssertNoDeadAxes(IEnumerable<string> inputAxes, IEnumerable<LayerRule> rules)
        {
            var referenced = rules.SelectMany(r => r.Required).Select(t => t.Axis)
                .ToHashSet(StringComparer.Ordinal);
            foreach (var axis in inputAxes)
                if (!referenced.Contains(axis))
                    throw new ArgumentException("输入轴 " + axis + " 未被任何规则引用（死轴）：请补规则或从坐标注册表移除");
        }
    }
}
