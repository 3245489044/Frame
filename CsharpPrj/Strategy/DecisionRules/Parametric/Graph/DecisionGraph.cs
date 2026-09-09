using System;
using System.Collections.Generic;
using System.Linq;

namespace Strategy.DecisionRules
{
    /// <summary>一次决策图求值的单层产出。</summary>
    public sealed record DecisionRunEntry(
        DecisionLayer Layer,
        SemanticTerm? Chosen,          // 命中最优规则产出的 (OutputAxis,label)；未命中 null
        IReadOnlyList<LayerRule> Hits, // 全部命中（审计同源：读全体命中而非只读胜者）
        bool Recomputed,               // 本次是否重算（false = 输入未变复用缓存）
        bool Skipped);                 // 是否被上游吸收标签短路剪枝（未求值）

    /// <summary>一次 <see cref="DecisionGraph.Compute"/> 的结果。</summary>
    public sealed class DecisionRun
    {
        public IReadOnlyList<DecisionRunEntry> Entries { get; }
        public IReadOnlySet<SemanticTerm> Active { get; } // 末态激活集（facts ∪ 各层产出词元；audit 读此）
        public DecisionRun(IReadOnlyList<DecisionRunEntry> entries, IReadOnlySet<SemanticTerm> active)
        {
            Entries = entries;
            Active = active;
        }

        public DecisionRunEntry? Of(string layerName)
            => Entries.FirstOrDefault(e => e.Layer.Name == layerName);

        /// <summary>某层产出标签（无命中/被跳过返回 null）。</summary>
        public string? LabelOf(string layerName)
            => Of(layerName)?.Chosen?.Term;
    }

    /// <summary>
    /// 决策图求值器（P-A 原型）——把多个 <see cref="DecisionLayer"/> 排成一张<b>按轴编排的有向无环图</b>：
    /// <list type="number">
    /// <item><b>feed-forward（一次嵌入、逐层恰好算一次）</b>：层按依赖拓扑求值；某层产出 (OutputAxis,label) 词元即写入激活集，
    ///       下游层可把它当输入轴引用（R3 输出轴层间回引；同一激活集被多面共享 = R2 共享证据）。</item>
    /// <item><b>短路剪枝</b>：某层命中吸收标签（TerminalLabels，Law A 语义）→ 其传递下游层被跳过、不再求值
    ///       （如 terminal→TM 后 route 不再激活 —— 对齐 TerminationPriority 传播截止）。</item>
    /// <item><b>增量 dirty 传播</b>：层按"消费证据指纹"记忆缓存；仅当某层消费的证据变化（上游产出或输入轴变）才重算，
    ///       未受影响层复用缓存（跨调用只重算受影响下游）。</item>
    /// </list>
    /// 本引擎<b>不改写</b> <see cref="SemanticVocabulary"/>/<see cref="SemanticClassRules"/>/<see cref="ParameterState"/> 语义，
    /// 只在其上提供"轴编排 + 固定参数通道(激活集词元) + 级联求值"的组合壳。
    /// </summary>
    public sealed class DecisionGraph
    {
        private readonly IReadOnlyList<DecisionLayer> _order;          // 拓扑序
        private readonly IReadOnlyDictionary<string, DecisionLayer> _byName;
        private readonly IReadOnlyDictionary<string, string> _axisOwner; // outputAxis -> layerName

        // 跨调用记忆：layerName -> (证据指纹, 上次产出)
        private readonly Dictionary<string, string?> _cachedLabel = new();
        private readonly Dictionary<string, string> _cachedFingerprint = new();

        /// <summary>层集合（可乱序；内部按依赖自动拓扑）。依赖 = 某层规则引用另一层输出轴词元。</summary>
        public DecisionGraph(IEnumerable<DecisionLayer> layers)
        {
            if (layers is null) throw new ArgumentNullException(nameof(layers));
            var all = layers.ToList();
            if (all.Count == 0) throw new ArgumentException("至少需要一个决策层。", nameof(layers));
            if (all.Select(l => l.Name).Distinct(StringComparer.Ordinal).Count() != all.Count)
                throw new ArgumentException("层名重复。", nameof(layers));
            if (all.Select(l => l.OutputAxis).Distinct(StringComparer.Ordinal).Count() != all.Count)
                throw new ArgumentException("层输出轴重复。", nameof(layers));

            _byName = all.ToDictionary(l => l.Name, StringComparer.Ordinal);
            _axisOwner = all.ToDictionary(l => l.OutputAxis, l => l.Name, StringComparer.Ordinal);

            // 依赖：L 依赖 P 当 L 的某规则引用 P.OutputAxis 上的词元
            var deps = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
            foreach (var l in all)
            {
                var needed = new HashSet<string>(StringComparer.Ordinal);
                foreach (var t in l.Rules.SelectMany(r => r.Required))
                    if (_axisOwner.TryGetValue(t.Axis, out var owner) && owner != l.Name)
                        needed.Add(owner);
                deps[l.Name] = needed;
            }

            // Kahn 拓扑 + 环检测
            var inDeg = all.ToDictionary(l => l.Name, l => deps[l.Name].Count, StringComparer.Ordinal);
            var ready = new SortedSet<string>(StringComparer.Ordinal);
            foreach (var (n, d) in inDeg) if (d == 0) ready.Add(n);
            var order = new List<DecisionLayer>();
            while (ready.Count > 0)
            {
                var name = ready.Min!;
                ready.Remove(name);
                order.Add(_byName[name]);
                foreach (var l in all)
                    if (deps[l.Name].Contains(name) && --inDeg[l.Name] == 0)
                        ready.Add(l.Name);
            }
            if (order.Count != all.Count)
                throw new InvalidOperationException("层依赖成环（ANTI-CYCLIC-AGGREGATION）：请勿让层输出环回上游。");
            _order = order;
        }

        public IReadOnlyList<DecisionLayer> Layers => _order;
        public int LayerCount => _order.Count;

        // ---- 求值主体 ----

        /// <summary>对一批事实（输入证据词元）求值一次，返回各层产出 + 短路/复用标注。</summary>
        public DecisionRun Compute(IEnumerable<SemanticTerm> facts)
        {
            var active = new HashSet<SemanticTerm>(facts ?? Enumerable.Empty<SemanticTerm>());
            var entries = new List<DecisionRunEntry>(_order.Count);
            var absorbedNow = new HashSet<string>(StringComparer.Ordinal); // 本 run 已吸收层

            foreach (var layer in _order)
            {
                // 短路剪枝：本层是某吸收层的传递下游 → 跳过、不重算也不复用。
                if (IsTransitiveDependentOf(layer.Name, absorbedNow))
                {
                    entries.Add(new DecisionRunEntry(layer, null, Array.Empty<LayerRule>(), Recomputed: false, Skipped: true));
                    continue;
                }

                var fp = Fingerprint(active, layer);
                if (_cachedFingerprint.TryGetValue(layer.Name, out var old) && old == fp)
                {
                    var cached = ToTerm(layer, _cachedLabel[layer.Name]);
                    // 缓存的产出若是吸收标签，仍须传播吸收（其下游可能未缓存）
                    if (_cachedLabel[layer.Name] is { } lab && layer.IsAbsorbing(lab)) absorbedNow.Add(layer.Name);
                    if (cached is { } c) active.Add(c); // 复用的产出词元同样写入激活集 → 下游可见（R3）
                    entries.Add(new DecisionRunEntry(layer, cached, Array.Empty<LayerRule>(), Recomputed: false, Skipped: false));
                    continue; // 消费证据未变 → 复用缓存（增量，不重算）
                }

                var hits = layer.Rules.Where(r => r.IsHit(active)).ToList();
                // hits 依 Rules 注册序生成 → 同优先级自然先注册者胜；无需再按索引 tie-break。
                var winner = hits.Count == 0 ? null : hits.OrderBy(r => r.Priority).First();

                var label = winner?.Label;
                _cachedLabel[layer.Name] = label;
                _cachedFingerprint[layer.Name] = fp;

                SemanticTerm? term = label is null ? null : new SemanticTerm(layer.OutputAxis, label);
                if (term is { } t) active.Add(t); // 输出轴词元写入激活集 → 下游可见（R3）
                if (label is not null && layer.IsAbsorbing(label)) absorbedNow.Add(layer.Name); // Law A 吸收

                entries.Add(new DecisionRunEntry(layer, term, hits, Recomputed: true, Skipped: false));
            }

            return new DecisionRun(entries, active);
        }

        /// <summary>某吸收层是否为本层（传递）祖先 → 本层短路跳过（本层上游含吸收层）。</summary>
        private bool IsTransitiveDependentOf(string layerName, IReadOnlySet<string> absorbed)
        {
            if (absorbed.Count == 0) return false;
            foreach (var a in absorbed)
                if (Reaches(layerName, a)) return true; // 沿本层上游(生产者)能否遇到吸收层 a
            return false;
        }

        /// <summary>从 <paramref name="from"/> 层沿"依赖-被依赖"边能否到达 <paramref name="target"/>（DFS）。</summary>
        private bool Reaches(string from, string target, HashSet<string>? seen = null)
        {
            seen ??= new HashSet<string>(StringComparer.Ordinal);
            if (!seen.Add(from)) return false;
            foreach (var p in DependenciesOf(from))
            {
                if (p == target) return true;
                if (Reaches(p, target, seen)) return true;
            }
            return false;
        }

        /// <summary>某层直接依赖的上游层（其规则引用它们输出轴的层）。</summary>
        private IEnumerable<string> DependenciesOf(string layerName)
        {
            var layer = _byName[layerName];
            var seen = new HashSet<string>(StringComparer.Ordinal);
            foreach (var t in layer.Rules.SelectMany(r => r.Required))
                if (_axisOwner.TryGetValue(t.Axis, out var owner) && owner != layerName && seen.Add(owner))
                    yield return owner;
        }

        /// <summary>层消费证据指纹：激活集里落在其输入轴（规则引用轴，排除自身输出轴）上的词元规范化串。</summary>
        private static string Fingerprint(IReadOnlySet<SemanticTerm> active, DecisionLayer layer)
        {
            var axes = layer.Rules.SelectMany(r => r.Required).Select(t => t.Axis)
                .Where(a => a != layer.OutputAxis).Distinct(StringComparer.Ordinal).ToHashSet(StringComparer.Ordinal);
            var keys = active.Where(t => axes.Contains(t.Axis)).Select(t => t.ToString()).OrderBy(x => x, StringComparer.Ordinal);
            return string.Join("|", keys);
        }

        private static SemanticTerm? ToTerm(DecisionLayer layer, string? label)
            => label is null ? null : new SemanticTerm(layer.OutputAxis, label);
    }
}
