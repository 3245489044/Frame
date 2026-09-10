using System;
using System.Collections.Generic;
using System.Linq;
using Strategy.DecisionRules;

namespace Strategy.DecisionRules.Configurable
{
    /// <summary>
    /// 配置驱动决策控制器（决策控制框架统一求值入口，域中立、LLM-free）——
    /// 把【决策依据（证据轴/层矩阵/吸收/跨轴关联/输出映射/兜底）】从 C# 代码外置为
    /// <see cref="DecisionControlSpec"/>，经 <see cref="IDecisionControlSource"/> 动态注入，
    /// 每次求值取当前依据演算。它复用既有 <see cref="DecisionGraph"/> 轴编排内核
    /// （拓扑/短路吸收/增量缓存），不建第二套求值引擎（SingleImplementation / TH-12）。
    /// <para>标准调用形态：</para>
    /// <code>controller.Evaluate(decisionPoint, evidence:(axis,term)[])</code>
    /// <para>输入装配与输出适配留给薄适配层；本控制器只做「取 Spec → 构筑/校验图 → 求值 → 包结果」。</para>
    /// </summary>
    public sealed class ConfigDrivenDecisionController
    {
        private readonly IDecisionControlSource _source;

        // 按 (decisionPoint) 缓存已构筑计划；Spec.Version 变才重建（动态演算：依据变 → 重演算，不变 → 复用）。
        private readonly Dictionary<string, ControlPlan> _cache = new(StringComparer.Ordinal);

        /// <param name="source">决策依据外部供给端口；null = 未装配（任何求值 → 未命中兜底）。</param>
        public ConfigDrivenDecisionController(IDecisionControlSource? source = null)
            => _source = source ?? NullControlSource.Instance;

        /// <summary>暴露当前 Expiration：按决策点查缓存计划是否存在（供热重载方比版本）。</summary>
        public int? CachedVersion(string decisionPoint)
            => _cache.TryGetValue(decisionPoint, out var p) ? p.Spec.Version : null;

        /// <summary>
        /// 求值唯一入口：取当前依据 →（版本变则重建图）→ 校验证据完备 → 求值 → 包通用结果。
        /// 未注册 / 依据缺失 → <see cref="DecisionControlResult.Matched"/>=false、<see cref="DecisionControlResult.Fallback"/>=true。
        /// </summary>
        /// <param name="evidence">输入证据（"axis=term" 元组；I+E 每轴恰一词元）。</param>
        public DecisionControlResult Evaluate(string decisionPoint, IReadOnlyList<(string Axis, string Term)>? evidence)
        {
            var plan = GetOrBuild(decisionPoint);
            if (plan is null)
                return Unmatched();

            var concrete = evidence ?? Array.Empty<(string, string)>();
            AssertEvidenceComplete(plan, concrete);            // 契约：轴内互斥完备，每输入轴恰一词元

            var terms = new List<SemanticTerm>(concrete.Count);
            foreach (var (a, t) in concrete)
                terms.Add(new SemanticTerm(a, t));

            var run = plan.Graph.Compute(terms);

            // 各层产出 + 全命中（审计同源：读全体命中而非只读胜者）。
            var layers = new List<DecisionLayerOutcome>(run.Entries.Count);
            foreach (var e in run.Entries)
                layers.Add(new DecisionLayerOutcome(
                    e.Layer.Name,
                    e.Chosen?.Term,
                    HitsOf(e),
                    e.Skipped));

            // 经 OutputMap 解析通用动作：优先命中层标签映射，否则兜底。
            var action = ResolveAction(plan, run)
                         ?? Resolve(plan.Spec.OutputMap, plan.Spec.Fallback);
            return new DecisionControlResult
            {
                Matched = true,
                Layers = layers,
                Action = action,
                Fallback = action != null && !run.Entries.Any(e => e.Chosen != null),
            };
        }

        // ---- 计划：Spec + 已构筑图 + 校验辅助 ----

        private sealed class ControlPlan
        {
            public required DecisionControlSpec Spec;
            public required DecisionGraph Graph;
            public required SemanticVocabulary Vocabulary;
            // axis → terms（校验/嵌入用）
            public required Dictionary<string, IReadOnlyList<string>> TermsByAxis;
            // 层 Name → OutputAxis（结果映射辅助）
            public required Dictionary<string, string> OutputAxisByLayer;
        }

        private ControlPlan? GetOrBuild(string decisionPoint)
        {
            if (_cache.TryGetValue(decisionPoint, out var cached))
                return cached;

            var spec = _source.Get(decisionPoint);
            if (spec is null) return null;

            var plan = BuildPlan(spec);
            Validate(plan);                                   // 构筑期校验（引用列/输出轴/标签/环/词元注册）
            _cache[decisionPoint] = plan;
            return plan;
        }

        /// <summary>由 Spec → 可复用 DecisionGraph（纯组装；拓扑/吸收/增量由内核承载）。</summary>
        private static ControlPlan BuildPlan(DecisionControlSpec spec)
        {
            // 证据轴 → 词表（唯一份；轴内词元互斥完备 = 向量索引序）。
            var termsByAxis = (spec.Evidence ?? new List<DecisionControlAxis>())
                .GroupBy(a => a.Axis ?? "")
                .ToDictionary(g => g.Key,
                    g => (IReadOnlyList<string>)g.SelectMany(a => a.Terms ?? new List<string>()).Distinct(StringComparer.Ordinal).ToArray(),
                    StringComparer.Ordinal);
            var vocabulary = new SemanticVocabulary(termsByAxis);

            // 具名跨轴关联：展开为词元实例（CrossFeatures）。
            var crossTerms = new Dictionary<string, SemanticTerm[]>(StringComparer.Ordinal);
            foreach (var c in spec.CrossFeatures ?? new List<DecisionControlCross>())
                crossTerms[c.Name ?? ""] = c.Terms
                    .Select(t => ParseTerm(t))
                    .OfType<SemanticTerm>()     // 过滤 null（SemanticTerm 为 struct，null=HasValue 为假）
                    .ToArray();

            // 层：规则按 Cross/When 展开，构建 DecisionLayer。
            var layers = new List<DecisionLayer>(spec.Layers?.Count ?? 0);
            foreach (var lc in spec.Layers ?? new List<DecisionControlLayer>())
            {
                var layer = new DecisionLayer(lc.Name ?? "", lc.Output ?? "", lc.Absorb);
                foreach (var r in lc.Rules ?? new List<DecisionControlRule>())
                {
                    var required = ResolveRequired(r, crossTerms);
                    if (required.Count == 0) continue;        // 无词元的规则无意义，跳过
                    layer.Add(new LayerRule(r.Label ?? "", r.Priority, required));
                }
                layers.Add(layer);
            }

            var graph = new DecisionGraph(layers);
            return new ControlPlan
            {
                Spec = spec,
                Graph = graph,
                Vocabulary = vocabulary,
                TermsByAxis = termsByAxis,
                OutputAxisByLayer = layers.ToDictionary(
                    l => l.Name, l => l.OutputAxis, StringComparer.Ordinal),
            };
        }

        /// <summary>把单条规则解析为必需词元集（Cross 引用整套，否则 When 内联解析）。</summary>
        private static List<SemanticTerm> ResolveRequired(DecisionControlRule r, IReadOnlyDictionary<string, SemanticTerm[]> crossTerms)
        {
            if (!string.IsNullOrWhiteSpace(r.Cross) && crossTerms.TryGetValue(r.Cross!, out var terms))
                return terms.ToList();
            var required = new List<SemanticTerm>();
            foreach (var w in r.When ?? new List<string>())
            {
                if (ParseTerm(w) is { } term) required.Add(term);
            }
            return required;
        }

        private static SemanticTerm? ParseTerm(string s)
        {
            var eq = s.IndexOf('=');
            if (eq <= 0) return null;
            return new SemanticTerm(s.Substring(0, eq).Trim(), s.Substring(eq + 1).Trim());
        }

        /// <summary>构筑期校验（对齐 DecisionCoordinateSchema.Validate 语义）：轴唯一 / 输出轴在 Evidence 且 O 区 /
        /// 标签与引用词元已注册 / 规则引用轴存在 / 层名与输出轴唯一。</summary>
        private static void Validate(ControlPlan plan)
        {
            var spec = plan.Spec;
            var axes = spec.Evidence ?? new List<DecisionControlAxis>();

            // 轴名唯一 + 轴内词元互斥（重复即抛，防坐标错位）。
            if (axes.GroupBy(a => a.Axis ?? "").Any(g => g.Count() > 1))
                throw new ArgumentException("决策控制 Spec：轴名重复。");
            var outAxes = axes.Where(a => a.Zone == "O").Select(a => a.Axis ?? "").ToHashSet(StringComparer.Ordinal);

            foreach (var lc in spec.Layers ?? new List<DecisionControlLayer>())
            {
                if (!outAxes.Contains(lc.Output ?? ""))
                    throw new ArgumentException($"决策控制 Spec：层 {lc.Name} 输出轴 {lc.Output} 未注册为 O 区轴。");
                foreach (var r in lc.Rules ?? new List<DecisionControlRule>())
                {
                    if (plan.Vocabulary.IndexOf(new SemanticTerm(lc.Output ?? "", r.Label ?? "")) < 0)
                        throw new ArgumentException($"决策控制 Spec：层 {lc.Name} 输出标签 {r.Label} 未在坐标注册。");
                    foreach (var req in ResolveRequired(r, new Dictionary<string, SemanticTerm[]>()))
                        if (plan.Vocabulary.IndexOf(req) < 0)
                            throw new ArgumentException($"决策控制 Spec：层 {lc.Name} 规则 {r.Label} 引用未注册词元 {req}。");
                }
            }
        }

        /// <summary>契约：I+E 每输入轴恰一词元（轴内互斥完备、无空档/多值）。</summary>
        private static void AssertEvidenceComplete(ControlPlan plan, IReadOnlyList<(string Axis, string Term)> evidence)
        {
            var inputAxes = plan.Spec.InputAxes().Select(a => a.Axis ?? "").ToHashSet(StringComparer.Ordinal);
            var byAxis = evidence.GroupBy(e => e.Axis).ToDictionary(g => g.Key, g => g.ToList());
            foreach (var axis in inputAxes)
            {
                if (!byAxis.TryGetValue(axis, out var list) || list.Count != 1)
                    throw new ArgumentException($"决策控制 Spec：输入轴 {axis} 必须恰有一个词元；实际 {(byAxis.TryGetValue(axis, out var l) ? l.Count : 0)}。");
                if (plan.Vocabulary.IndexOf(new SemanticTerm(axis, byAxis[axis][0].Term)) < 0)
                    throw new ArgumentException($"决策控制 Spec：输入轴 {axis} 词元未注册：{byAxis[axis][0].Term}。");
            }
        }

        // ---- 输出解析 / 审计 ----

        /// <summary>依各层命中标签经 OutputMap 解析动作（key "axis=label"；对拍/适配用力：优先顶优先级命中者）。</summary>
        private static string? ResolveAction(ControlPlan plan, DecisionRun run)
        {
            if (plan.Spec.OutputMap is null) return null;
            // 遍历拓扑序层，取首个命中且有映射的标签。
            foreach (var e in run.Entries)
            {
                if (e.Chosen is not { } chosen) continue;
                var key = chosen.Axis + "=" + chosen.Term;
                if (plan.Spec.OutputMap.TryGetValue(key, out var action))
                    return action;
            }
            return null;
        }

        private static string? Resolve(IReadOnlyDictionary<string, string>? map, string? fallbackKey)
        {
            if (fallbackKey is null) return null;
            if (map is not null && map.TryGetValue(fallbackKey, out var v)) return v;
            return fallbackKey;
        }

        private static IReadOnlyList<string> HitsOf(DecisionRunEntry e)
            => e.Hits.Select(h => h.Label).ToArray();

        private static DecisionControlResult Unmatched()
            => new DecisionControlResult { Matched = false, Layers = Array.Empty<DecisionLayerOutcome>(), Fallback = true };
    }
}