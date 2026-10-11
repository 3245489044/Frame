using System.Collections.Generic;
using ParameterResolver.KeyMapping;
using Share.HierarchySupport;

namespace Operation.HierarchyMethod.Nodes.Workflows
{
    /// <summary>骨架来源标记：声明式模板加载 或 内置默认回退（可观测，区分「声明生效」与「静默回退」）。</summary>
    public enum RoundSpecSource
    {
        /// <summary>声明式工作流模板加载（Config/workflows/*.json 真实生效）。</summary>
        Declared,

        /// <summary>内置默认骨架回退（模板缺失/解析失败时兜底）。</summary>
        BuiltinDefault,
    }

    /// <summary>成功判定准则：由模板声明，解耦「工作流跑完」与「产出决策词」两类语义。</summary>
    public enum RoundSpecSuccessCriterion
    {
        /// <summary>须产出决策词（decision∈{stop/complete/continue}）方为成功（主循环 agent-loop 语义）。</summary>
        DecisionWord,

        /// <summary>无异常跑完主路径即成功（会话/线性链模板语义，如 collect-analyze-act）。</summary>
        RunToEnd,
    }

    /// <summary>
    /// 骨架步骤：按名字调用注册函数，从数据空间解析入参、回写结果键。可带节点级入/出参映射。
    /// <see cref="NodeId"/> 为**稳定节点实例 ID**（声明式模板的 NodeId；缺失时回退 <see cref="FuncName"/>），
    /// 作为节点存储主键（NodeDefinition.NodeKey / RoundPipelineContext 步骤键），杜绝「同名函数（FuncName）多次出现 → 节点覆盖」。
    /// </summary>
    public sealed record RoundStep(
        string FuncName,
        IReadOnlyList<string> ParameterKeys,
        IReadOnlyList<string> ResultKeys,
        KeyMappingConfig? InputMapping = null,
        KeyMappingConfig? OutputMapping = null,
        string? NodeId = null)
    {
        /// <summary>稳定节点实例 ID：优先模板声明的 NodeId，缺失（旧模板/内置骨架）回退 FuncName。</summary>
        public string EffectiveNodeId => string.IsNullOrWhiteSpace(NodeId) ? FuncName : NodeId;

    /// <summary>
    /// 本步发射的观测事件分类（<c>[Flow]</c> 步骤的 <c>emit=k1,k2</c> 绑定；空 = 不发射）。
    /// 加法式扩展：缺省空集合，旧模板/内置骨架零影响；埋点由宿主编排（Usharp 适配层）消费，执行层不解释。
    /// </summary>
    public IReadOnlyList<string> Emits { get; init; } = Array.Empty<string>();

    /// <summary>
    /// 声明式执行体分类（<c>[Node] kind=</c>；G-2）：<c>internal</c>/<c>external</c>/<c>composition</c>；
    /// 缺省 null = 不注入（零回归）。由 <see cref="UsharpNodeSpec.Kind"/> 经发射器传播，
    /// 供 <c>call_func</c> 声明驱动分派消费（RoundStepInvoker 在 fn=call_func 时注入入参 kind）。
    /// 加法式扩展：旧模板/内置骨架零影响。
    /// </summary>
    public string? Kind { get; init; }

    /// <summary>
    /// 入史标记声明（CP-4 声明面）：<c>Numbered</c>（显式声明中间节点入史，供分层气泡）/ <c>None</c> / <c>Terminal</c>；
    /// 缺省 null = 由 <c>HistoryMarkPolicy</c> 默认规则裁决（终答默认 Terminal，其余 None）。
    /// 加法式扩展：缺省 null，旧模板/内置骨架零影响。
    /// </summary>
    public string? Mark { get; init; }

    /// <summary>
    /// 声明式依赖边（谁调谁由声明面给出）：本步的**前驱步骤键**（<see cref="EffectiveNodeId"/> 域）。
    /// 非空（任一步骤声明）⇒ 电路投影以**声明**为准：Wire 只在声明的前驱→后继之间按端口交集生成，
    /// 未声明的真数据依赖（消费键由更早步骤产出）在编译期判红 —— 不再由「前驱产出 → 后继消费」键名猜测。
    /// 缺省 null（不声明）⇒ 回退既有键名派生，旧模板/内置骨架行为逐字不变（加法式扩展）。
    /// <para>
    /// 声明的前驱与后继**无共同数据键**时 = **控制边**（仅约束先后，不经数据键）：
    /// 投影为 Sequence 镜像（不产 Wire），值面仍走 ctx / KeyMapping / ur_nodes。
    /// </para>
    /// <para>**中立原语**：禁止写成框架类型（本工程不得新开 → <c>Share.CircuitFramework</c> 引用边）。</para>
    /// </summary>
    public IReadOnlyList<string>? DependsOn { get; init; }

        /// <summary>转换为共享绑定契约（供 DataContainerBinder 消费）。</summary>
        public BindingStep ToBindingStep() => new()
        {
            ParameterKeys = ParameterKeys,
            ResultKeys = ResultKeys,
            InputMapping = InputMapping,
            OutputMapping = OutputMapping,
        };
    }

    /// <summary>
    /// 一轮骨架：有序步骤列表（声明式数据，而非散落的硬编码逻辑）。
    /// 无状态容器 —— 仅保留步骤 + 端口契约 + 全局键映射，运行时状态全部外置 StateCenter。
    /// 本类型为通用工作流骨架契约（从 Agent.Contract.Workflows 下沉，SingleImplementation / TH-12），
    /// 供 HierarchyMethod.Nodes 关系编排（WorkflowReferenceStaticizer/WorkflowResultDeriver）与 Agent 工作流执行共同消费。
    /// </summary>
    public sealed record RoundSpec(
        string Name,
        IReadOnlyList<RoundStep> Steps,
        IReadOnlyList<PortSpec>? InputPorts = null,
        IReadOnlyList<PortSpec>? OutputPorts = null,
        KeyMappingConfig? GlobalKeyMapping = null)
    {
        /// <summary>来源标记：声明式模板加载 或 内置默认回退（缺省声明式；内置默认骨架构造时显式置 BuiltinDefault）。</summary>
        public RoundSpecSource Source { get; init; } = RoundSpecSource.Declared;

        /// <summary>基类工作流模板名（对齐 C# 类继承 is-a）：非空时经 Inherits 边声明继承，派生复用基类步骤 + 覆写同名 NodeId（对齐 override）。</summary>
        public string? BaseWorkflow { get; init; }

        /// <summary>成功判定准则（缺省 DecisionWord，保持主循环语义；会话/线性链模板声明 RunToEnd）。</summary>
        public RoundSpecSuccessCriterion SuccessCriterion { get; init; } = RoundSpecSuccessCriterion.DecisionWord;

        /// <summary>
        /// 分支声明（R3 中立原语，加法式；缺省空 = 无分支）：判定键为真 → then 激活 / else 抑制。
        /// 由 <c>RoundFlowSpecMapper</c> 映射为电路配方分支（框架只承载结构）。
        /// </summary>
        public IReadOnlyList<RoundBranch>? Branches { get; init; }

        /// <summary>多路分派声明（R3 中立原语，加法式；缺省空 = 无分派）。</summary>
        public IReadOnlyList<RoundSwitch>? Switches { get; init; }

        /// <summary>调用声明（R3 中立原语，加法式；缺省空 = 无调用）：承载节点调用子骨架。</summary>
        public IReadOnlyList<RoundCall>? Calls { get; init; }

        /// <summary>区域声明（R3+ 中立原语，加法式；缺省空 = 无区域）：父层单节点 + 子计划迭代计划。</summary>
        public IReadOnlyList<RoundRegion>? Regions { get; init; }

        /// <summary>受控边声明（R3+ 中立原语，加法式；缺省空 = 无受控边）：判定判真后边激活。</summary>
        public IReadOnlyList<RoundWhenDecl>? Whens { get; init; }

        /// <summary>受护作用域声明（R3+ 中立原语，加法式；缺省空 = 无受护）：失败吞并继续。</summary>
        public IReadOnlyList<RoundGuardDecl>? Guards { get; init; }

        /// <summary>转换为共享绑定契约（供 DataContainerBinder 消费）。</summary>
        public BindingContainer ToBindingContainer() => new()
        {
            InputPorts = InputPorts,
            OutputPorts = OutputPorts,
            GlobalKeyMapping = GlobalKeyMapping,
        };
    }
}
