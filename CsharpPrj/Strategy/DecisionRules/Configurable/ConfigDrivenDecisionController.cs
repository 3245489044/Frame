using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Security.Cryptography;
using System.Text;
using BasicMethod.Declarative.Graph;
using BasicMethod.Declarative.Spec;
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

        // 按 (decisionPoint) 缓存已构筑计划；内容指纹变才重建（动态演算：依据变 → 重演算，不变 → 复用）。
        // 并发安全：读多写少、构筑幂等，采用并发字典；同指纹并发构筑只会产生等价计划（后者覆盖，语义不变）。
        private readonly ConcurrentDictionary<string, ControlPlan> _cache = new(StringComparer.Ordinal);

        // 构筑失败负缓存（decisionPoint → 指纹与首个异常）：同指纹不再重复构筑并抛。
        private readonly ConcurrentDictionary<string, BuildFailure> _failures = new(StringComparer.Ordinal);

        /// <param name="source">决策依据外部供给端口；null = 未装配（任何求值 → 未命中兜底）。</param>
        public ConfigDrivenDecisionController(IDecisionControlSource? source = null)
            => _source = source ?? NullControlSource.Instance;

        /// <summary>当前已构筑计划的规格版本（未构筑/已失效返回 null）；供外部探询热重载进度（失效判定实际按内容指纹）。</summary>
        public int? CachedVersion(string decisionPoint)
            => _cache.TryGetValue(decisionPoint, out var p) ? p.Spec.Version : null;

        /// <summary>
        /// 求值唯一入口：取当前依据 →（内容指纹变则重建图）→ 校验证据完备 → 求值 → 包通用结果。
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

            // DecisionGraph 的跨调用增量缓存非线程安全：按计划互斥，避免并发求值读写缓存竞态。
            // 锁粒度 = 单决策点计划（非全局），仅保护该图实例的求值临界区。
            DecisionRun run;
            lock (plan.Gate) { run = plan.Graph.Compute(terms); }

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

            // Matched 如实反映「是否至少一层产出标签」：全层未命中 → false，包装层据此回退代码矩阵。
            var anyChosen = run.Entries.Any(e => e.Chosen != null);
            return new DecisionControlResult
            {
                Matched = anyChosen,
                Layers = layers,
                Action = action,
                Fallback = !anyChosen,
            };
        }

        // ---- 计划：Spec + 已构筑图 + 校验辅助 ----

        private sealed class ControlPlan
        {
            public required DecisionControlSpec Spec;
            public required DecisionGraph Graph;
            public required SemanticVocabulary Vocabulary;
            /// <summary>构筑此计划时的规格内容指纹（缓存失效依据）。</summary>
            public required string Fingerprint;
            /// <summary>计划级互斥：DecisionGraph 的跨调用增量缓存非线程安全，求值临界区按此加锁。</summary>
            public readonly object Gate = new();
            // axis → terms（校验/嵌入用）
            public required Dictionary<string, IReadOnlyList<string>> TermsByAxis;
            // 层 Name → OutputAxis（结果映射辅助）
            public required Dictionary<string, string> OutputAxisByLayer;
        }

        /// <summary>
        /// 取当前依据并（<b>内容指纹</b>变则重建）返回可复用计划。
        /// 指纹比较是热重载的正确性根：源重载出新 Spec 后，即便手工 <c>Version</c> 未变，只要内容变了，
        /// 指纹必然不同 → 重建；若此处只比 <c>Version</c>，恒写 1 的源（如 .ushar 供给）将永远命中旧计划。
        /// 同指纹的构筑失败做<b>负缓存</b>：不再每轮重复 BuildPlan+Validate 并抛，直接复抛首次异常。
        /// </summary>
        private ControlPlan? GetOrBuild(string decisionPoint)
        {
            var spec = _source.Get(decisionPoint);
            if (spec is null)
            {
                _failures.TryRemove(decisionPoint, out _);    // 依据缺失 → 清失败态，交未命中兜底
                return null;
            }

            var fingerprint = FingerprintOf(spec);
            if (_cache.TryGetValue(decisionPoint, out var cached) && cached.Fingerprint == fingerprint)
                return cached;

            if (_failures.TryGetValue(decisionPoint, out var failed) && failed.Fingerprint == fingerprint)
                failed.Error.Throw();                         // 同指纹已知非法 → 复抛（保留原始栈），不重复构筑

            try
            {
                var plan = BuildPlan(spec, fingerprint);
                Validate(plan);                               // 构筑期校验（引用列/输出轴/标签/环/词元注册）
                _cache[decisionPoint] = plan;
                _failures.TryRemove(decisionPoint, out _);
                return plan;
            }
            catch (Exception ex)
            {
                _cache.TryRemove(decisionPoint, out _);       // 非法新版不留旧计划（避免指纹/计划错配）
                _failures[decisionPoint] = new BuildFailure(fingerprint, ExceptionDispatchInfo.Capture(ex));
                throw;
            }
        }

        /// <summary>构筑失败负缓存条目（内容指纹 → 首次异常，供同指纹复抛）。</summary>
        private sealed record BuildFailure(string Fingerprint, ExceptionDispatchInfo Error);

        /// <summary>
        /// 规格内容指纹：源显式提供 <see cref="DecisionControlSpec.Fingerprint"/> 时直接采用；
        /// 否则对规格中<b>全部影响求值的内容</b>（决策点、版本、证据轴与词序、跨轴关联、层/规则/优先级/条件、
        /// 输出映射、兜底）做序规范化后取 SHA256。
        /// <para>为何内容一变必然变：上述字段是 <see cref="BuildPlan"/> 的全部输入，任一求值相关字段变动都会改变
        /// 规范化串；词元按长度前缀编码，避免拼接分隔符造成不同内容同串。</para>
        /// </summary>
        private static string FingerprintOf(DecisionControlSpec spec)
        {
            if (!string.IsNullOrWhiteSpace(spec.Fingerprint)) return spec.Fingerprint!;

            var sb = new StringBuilder(256);
            sb.Append("V;").Append(spec.Version).Append(';');
            Append(sb, spec.DecisionPoint);

            foreach (var a in spec.Evidence ?? Enumerable.Empty<DecisionControlAxis>())
            {
                sb.Append("A;");
                Append(sb, a.Zone);
                Append(sb, a.Axis);
                foreach (var t in a.Terms ?? Enumerable.Empty<string>()) Append(sb, t);
                sb.Append('|');
            }

            foreach (var c in spec.CrossFeatures ?? Enumerable.Empty<DecisionControlCross>())
            {
                sb.Append("C;");
                Append(sb, c.Name);
                // HashSet 枚举序跨进程不稳定（S4）：按序规范化，指纹只认内容不认枚举顺序。
                foreach (var t in c.Terms.OrderBy(x => x, StringComparer.Ordinal)) Append(sb, t);
                sb.Append('|');
            }

            foreach (var l in spec.Layers ?? Enumerable.Empty<DecisionControlLayer>())
            {
                sb.Append("L;");
                Append(sb, l.Name);
                Append(sb, l.Output);
                foreach (var ab in l.Absorb ?? Enumerable.Empty<string>()) Append(sb, ab);
                sb.Append(';');
                // 规则按声明序（同优先级靠声明序 tie-break），故不排序。
                foreach (var r in l.Rules ?? Enumerable.Empty<DecisionControlRule>())
                {
                    sb.Append("R;");
                    Append(sb, r.Label);
                    sb.Append(r.Priority).Append(';');
                    Append(sb, r.Cross);
                    foreach (var w in r.When ?? Enumerable.Empty<string>()) Append(sb, w);
                    sb.Append(';');
                }
            }

            if (spec.OutputMap is not null)
                foreach (var kv in spec.OutputMap.OrderBy(x => x.Key, StringComparer.Ordinal))
                {
                    sb.Append("M;");
                    Append(sb, kv.Key);
                    Append(sb, kv.Value);
                }

            sb.Append("F;");
            Append(sb, spec.Fallback);

            return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(sb.ToString())));
        }

        /// <summary>长度前缀编码（消歧）：词元/名称自身含分隔符时也不会与另一组内容拼出同一串。</summary>
        private static void Append(StringBuilder sb, string? value)
            => sb.Append(value?.Length ?? 0).Append(':').Append(value ?? "").Append(';');

        /// <summary>由 Spec → 可复用 DecisionGraph（纯组装；拓扑/吸收/增量由内核承载）。</summary>
        private static ControlPlan BuildPlan(DecisionControlSpec spec, string fingerprint)
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
                Fingerprint = fingerprint,
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