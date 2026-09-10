using System.Collections.Generic;
using System.Linq;
using Strategy.DecisionRules;

namespace Strategy.DecisionRules.Configurable
{
    // ======================================================================
    //  配置承载的「决策依据」：从外部 JSON/源动态注入，把分散在 C# 代码里的
    //  （证据轴 / 层矩阵 / 吸收标签 / 层间引用 / 输出映射 / 兜底）全部数据化。
    //  本文件只声明 Spec 静态结构，不承载裁决；构筑/求值唯一在 ConfigDrivenDecisionController。
    // ======================================================================

    /// <summary>证据轴声明（I/E/O 分区；轴内词元集声明序即向量索引序 = 词元互斥完备）。</summary>
    public sealed class DecisionControlAxis
    {
        /// <summary>轴所属区：I(输入证据) / E(引擎参数带) / O(裁决输出)。</summary>
        public string? Zone { get; set; }
        /// <summary>轴名（如 round_signal / intent / eval / route）。</summary>
        public string? Axis { get; set; }
        /// <summary>该轴合法词元集（轴内互斥、完备）。</summary>
        public List<string>? Terms { get; set; }
    }

    /// <summary>一条层内规则（当 = 全部必需词元命中，AND；可引上游层输出轴词元 = R3 层间回引）。</summary>
    public sealed class DecisionControlRule
    {
        /// <summary>输出标签（落在所属层 OutputAxis 词元集）。</summary>
        public string? Label { get; set; }
        /// <summary>面内显式优先级，越小越优先（Law D）；同优先按声明序。</summary>
        public int Priority { get; set; }
        /// <summary>引用具名跨轴关联（CrossFeatures 键；与 When 互斥，命中即展开为该 CrossFeature 词元）。</summary>
        public string? Cross { get; set; }
        /// <summary>内联必需词元（"axis=term"，最多跨轴 AND）；与 Cross 互斥。</summary>
        public List<string>? When { get; set; }
    }

    /// <summary>一个决策层（= 一条输出轴 + 组面内互斥规则 + 终值吸收标签集）。</summary>
    public sealed class DecisionControlLayer
    {
        public string? Name { get; set; }
        /// <summary>层专属输出轴（如 eval / terminal / route）。</summary>
        public string? Output { get; set; }
        /// <summary>命中即吸收并短路下游的标签集（Law A）；可空。</summary>
        public List<string>? Absorb { get; set; }
        public List<DecisionControlRule>? Rules { get; set; }
    }

    /// <summary>具名跨轴关联（R2）：一处定义、多面共享。Terms 为 "axis=term" 词元；Author 承载层 Name→标签（供产物内自引用）。</summary>
    public sealed class DecisionControlCross
    {
        public string? Name { get; set; }
        public HashSet<string> Terms { get; set; } = new(System.StringComparer.Ordinal);
        /// <summary>（可选）层名 → 该层标签：图运行时把本关联的动态产物词元追加到 Terms。</summary>
        public string? Layer { get; set; }
        public string? Label { get; set; }
    }

    /// <summary>
    /// 决策控制规格（一份 = 一个决策点的完整决策依据，可 JSON 化 / 版本化 / 热重载）。
    /// 对应方案「决策依据配置化」：Evidence=证据坐标注册表，Layers=三面矩阵，
    /// OutputMap=通用标签→消费方动作/终态映射，Fallback=兜底。
    /// </summary>
    public sealed class DecisionControlSpec
    {
        /// <summary>决策点键（与 IDecisionControlSource.Get 的一致身份；如 agent.loop.eval）。</summary>
        public string? DecisionPoint { get; set; }

        /// <summary>规格版本：控制器按版本缓存已构筑 DAG，版本变才重建（动态演算 + 增量不改的依据复用）。</summary>
        public int Version { get; set; } = 1;

        /// <summary>证据轴注册表（I+E+O 全覆盖；I/E 为输入轴，O 为输出轴）。</summary>
        public List<DecisionControlAxis>? Evidence { get; set; }

        /// <summary>具名跨轴关联（可选）。</summary>
        public List<DecisionControlCross>? CrossFeatures { get; set; }

        /// <summary>层集（可乱序；内部按依赖自动拓扑，如 eval→terminal→route）。</summary>
        public List<DecisionControlLayer>? Layers { get; set; }

        /// <summary>通用标签 → 消费方动作/终态映射（键 "axis=label"，如 "eval=pass"→"advance"）。域中立，由适配层翻译。
        /// 注：Agent 轮内/路由路径<b>不消费</b>本字段（只读标签），ur_config 翻译等场景使用。</summary>
        public Dictionary<string, string>? OutputMap { get; set; }

        /// <summary>无命中/未注册的兜底标签（fail-safe stay/continue）；null = 引擎不匹配。
        /// 注：Agent 轮内/路由路径<b>不消费</b>本字段（未命中由包装层回退代码矩阵）。</summary>
        public string? Fallback { get; set; }

        // ---- 便捷访问 ----
        public List<DecisionControlAxis> OutputAxes()
            => Evidence?.Where(a => a.Zone == "O").ToList() ?? new List<DecisionControlAxis>();
        public List<DecisionControlAxis> InputAxes()
            => Evidence?.Where(a => a.Zone != "O").ToList() ?? new List<DecisionControlAxis>();
    }

    // ======================================================================
    //  求值结果信封（域中立，LLM-free）：只含通用标签 + 全命中 + 动作 + 审计，
    //  不含任何 Agent 类型；由 Agent 适配层翻译为 SchedulerAction / LoopDecision / EvalOutcome 等。
    // ======================================================================

    /// <summary>单个决策层的求值产出。</summary>
    public sealed record DecisionLayerOutcome(
        string LayerName,
        string? Label,                      // 命中最优标签；未命中 null
        IReadOnlyList<string> Hits,         // 全命中标签（审计同源，非互斥胜者）
        bool Skipped);                      // 是否被上游吸收标签短路剪枝

    /// <summary>控制器一次求值结果。</summary>
    public sealed class DecisionControlResult
    {
        public bool Matched { get; init; }
        /// <summary>各层产出（拓扑序）。</summary>
        public IReadOnlyList<DecisionLayerOutcome> Layers { get; init; } = System.Array.Empty<DecisionLayerOutcome>();
        /// <summary>经 OutputMap 解析出的通用动作/终态标签；无则 null。</summary>
        public string? Action { get; init; }
        /// <summary>兜底是否生效（无命中/未注册匹配）。</summary>
        public bool Fallback { get; init; }

        public string? LabelOf(string layerName)
            => Layers.FirstOrDefault(l => l.LayerName == layerName)?.Label;
        public bool Skipped(string layerName)
            => Layers.FirstOrDefault(l => l.LayerName == layerName)?.Skipped ?? false;
    }
}