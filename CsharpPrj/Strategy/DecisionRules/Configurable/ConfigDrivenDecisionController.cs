using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
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

        // 构筑失败负缓存（decisionPoint → 首个失败版本与异常）：同版本不再重复构筑并抛。
        private readonly Dictionary<string, BuildFailure> _failures = new(StringComparer.Ordinal);

        /// <param name="source">决策依据外部供给端口；null = 未装配（任何求值 → 未命中兜底）。</param>
        public ConfigDrivenDecisionController(IDecisionControlSource? source = null)
            => _source = source ?? NullControlSource.Instance;

        /// <summary>当前已构筑计划的规格版本（未构筑/已失效返回 null）；供外部探询热重载进度。</summary>
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

        /// <summary>
        /// 取当前依据并（版本变则重建）返回可复用计划。
        /// 版本比较是热重载的正确性根：源已按 LastWrite 重载出新 Spec，若此处不比版本即命中缓存，
        /// 消费方将永远拿到旧计划（配置热重载对该控制器失效）。
        /// 同版本的构筑失败做<b>负缓存</b>：不再每轮重复 BuildPlan+Validate 并抛，直接复抛首次异常。
        /// </summary>
        private ControlPlan? GetOrBuild(string decisionPoint)
        {
            var spec = _source.Get(decisionPoint);
            if (spec is null)
            {
                _failures.Remove(decisionPoint);              // 依据缺失 → 清失败态，交未命中兜底
                return null;
            }

            if (_cache.TryGetValue(decisionPoint, out var cached) && cached.Spec.Version == spec.Version)
                return cached;

            if (_failures.TryGetValue(decisionPoint, out var failed) && failed.Version == spec.Version)
                failed.Error.Throw();                         // 同版本已知非法 → 复抛（保留原始栈），不重复构筑

            try
            {
                var plan = BuildPlan(spec);
                Validate(plan);                               // 构筑期校验（引用列/输出轴/标签/环/词元注册）
                _cache[decisionPoint] = plan;
                _failures.Remove(decisionPoint);
                return plan;
            }
            catch (Exception ex)
            {
                _cache.Remove(decisionPoint);                 // 非法新版不留旧计划（避免版本/计划错配）
                _failures[decisionPoint] = new BuildFailure(spec.Version, ExceptionDispatchInfo.Capture(ex));
                throw;
            }
        }

        /// <summary>构筑失败负缓存条目（版本 → 首次异常，供同版本复抛）。</summary>
        private sealed record BuildFailure(int Version, ExceptionDispatchInfo Error);

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

        /// <summary>构筑期校验（唯一实现下沉 <see cref="CoordinateInvariants"/>，D3）：轴名唯一 + 坐标一致性（输出轴/标签/引用/死轴）。</summary>
        private static void Validate(ControlPlan plan)
        {
            var spec = plan.Spec;
            var axes = spec.Evidence ?? new List<DecisionControlAxis>();

            // 轴名唯一（重复即抛，防坐标错位）。
            if (axes.GroupBy(a => a.Axis ?? "").Any(g => g.Count() > 1))
                throw new ArgumentException("决策控制 Spec：轴名重复。");

            CoordinateInvariants.ValidateGraph(
                plan.Vocabulary,
                spec.OutputAxes().Select(a => a.Axis ?? ""),
                spec.InputAxes().Select(a => a.Axis ?? ""),
                plan.Graph);
        }

        /// <summary>契约：I+E 每输入轴恰一词元（唯一实现 <see cref="CoordinateInvariants.AssertEvidenceComplete"/>）。</summary>
        private static void AssertEvidenceComplete(ControlPlan plan, IReadOnlyList<(string Axis, string Term)> evidence)
            => CoordinateInvariants.AssertEvidenceComplete(
                plan.Vocabulary, plan.Spec.InputAxes().Select(a => a.Axis ?? ""), evidence);

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