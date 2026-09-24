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
    /// 入史标记声明（CP-4 声明面）：<c>Numbered</c>（显式声明中间节点入史，供分层气泡）/ <c>None</c> / <c>Terminal</c>；
    /// 缺省 null = 由 <c>HistoryMarkPolicy</c> 默认规则裁决（终答默认 Terminal，其余 None）。
    /// 加法式扩展：缺省 null，旧模板/内置骨架零影响。
    /// </summary>
    public string? Mark { get; init; }

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

        /// <summary>转换为共享绑定契约（供 DataContainerBinder 消费）。</summary>
        public BindingContainer ToBindingContainer() => new()
        {
            InputPorts = InputPorts,
            OutputPorts = OutputPorts,
            GlobalKeyMapping = GlobalKeyMapping,
        };
    }
}
