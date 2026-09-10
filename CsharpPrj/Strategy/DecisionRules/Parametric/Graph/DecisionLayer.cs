using System;
using System.Collections.Generic;
using System.Linq;

namespace Strategy.DecisionRules
{
    /// <summary>
    /// 一条规则：标签在「全部必需词元命中」时成立（语义 = 跨轴 AND，等价 WhenAllOf 的判别语义）。
    /// 优先级（Law D）为<b>面内</b>显式支配，数值越小越优先；同优先级按注册序（先注册先胜）。跨面无优先级竞争（各面答各问题）。
    /// </summary>
    public sealed class LayerRule
    {
        public string Label { get; }
        public int Priority { get; }
        public IReadOnlyList<SemanticTerm> Required { get; }

        /// <param name="label">输出标签（落在所属层的 OutputAxis 词元集）。</param>
        /// <param name="priority">面内显式优先级，越小越优先（Law D）。</param>
        /// <param name="required">触发所需词元（AND；可跨轴，可含上游层输出轴词元 = R3 层间回引）。</param>
        public LayerRule(string label, int priority, IEnumerable<SemanticTerm> required)
        {
            if (string.IsNullOrWhiteSpace(label)) throw new ArgumentException("规则标签不允许空白。", nameof(label));
            if (priority < 0) throw new ArgumentOutOfRangeException(nameof(priority), "优先级不允许为负。");
            if (required is null) throw new ArgumentNullException(nameof(required));
            var req = required.ToList();
            if (req.Count == 0) throw new ArgumentException("至少需要一个必需词元。", nameof(required));
            Label = label;
            Priority = priority;
            Required = req;
        }

        /// <summary>该规则是否命中给定激活集（全部必需词元在激活集中）。</summary>
        public bool IsHit(IReadOnlySet<SemanticTerm> active)
            => Required.All(active.Contains);

        public override string ToString() => $"{Label}@{Priority}:[{string.Join(" ∧ ", Required)}]";
    }

    /// <summary>
    /// 共享跨轴关联（设计 R2 语义）：一个<b>具名</b>跨轴词元条件（如 intent_fail = round_signal.done ∧ intent.tool_expected ∧ produced.no）。
    /// 定义一处、可被多个 <see cref="DecisionLayer"/> 的规则引用（引用同一实例即共享），实现"同一条跨轴关联被多面共享、一次组装"。
    /// </summary>
    public sealed record CrossFeature(string Name, IReadOnlyList<SemanticTerm> Terms)
    {
        public CrossFeature(string name, IEnumerable<SemanticTerm> terms)
            : this(name, (terms ?? throw new ArgumentNullException(nameof(terms))).ToList())
        {
            if (string.IsNullOrWhiteSpace(Name)) throw new ArgumentException("共享跨轴关联名不允许空白。", nameof(name));
            if (Terms.Count == 0) throw new ArgumentException("共享跨轴关联至少需要一个词元。", nameof(terms));
        }
    }

    /// <summary>
    /// 决策层（设计 R1/R3）：一个节点 = 一个输出轴 + 一组面内互斥规则 + 终值吸收标签集。
    /// 层在图上按依赖拓扑只算一次；命中最优规则即产出 <c>(OutputAxis, label)</c> 词元，写入激活集供下游层引用（输出轴可作下一层输入轴）。
    /// <c>TerminalLabels</c> = 该层命中即<b>吸收</b>（Law A 语义）的标签集：命中后下游层被短路剪枝（如 terminal→TM 后 route 不再激活）。
    /// </summary>
    public sealed class DecisionLayer
    {
        private readonly List<LayerRule> _rules = new();

        /// <param name="name">层名（图内唯一）。</param>
        /// <param name="outputAxis">该层专属输出轴（如 eval / terminal / route，或任意域自定义轴名）。</param>
        /// <param name="terminalLabels">命中即吸收并短路下游的标签集（可空）。</param>
        public DecisionLayer(string name, string outputAxis, IEnumerable<string>? terminalLabels = null)
        {
            if (string.IsNullOrWhiteSpace(name)) throw new ArgumentException("层名不允许空白。", nameof(name));
            if (string.IsNullOrWhiteSpace(outputAxis)) throw new ArgumentException("输出轴不允许空白。", nameof(outputAxis));
            Name = name;
            OutputAxis = outputAxis;
            TerminalLabels = terminalLabels is null
                ? new HashSet<string>()
                : new HashSet<string>(terminalLabels);
        }

        public string Name { get; }
        public string OutputAxis { get; }
        public IReadOnlySet<string> TerminalLabels { get; }
        public IReadOnlyList<LayerRule> Rules => _rules;

        /// <summary>加一条规则。</summary>
        public DecisionLayer Add(LayerRule rule)
        {
            if (rule is null) throw new ArgumentNullException(nameof(rule));
            _rules.Add(rule);
            return this;
        }

        /// <summary>便捷：加一条 <see cref="CrossFeature"/> 引用的规则（同一条跨轴关联被多面共享的接线点）。</summary>
        public DecisionLayer Add(string label, int priority, CrossFeature crossFeature)
            => Add(new LayerRule(label, priority, crossFeature.Terms));

        /// <summary>该层是否命中吸收标签。</summary>
        public bool IsAbsorbing(string label) => TerminalLabels.Contains(label);
    }
}
