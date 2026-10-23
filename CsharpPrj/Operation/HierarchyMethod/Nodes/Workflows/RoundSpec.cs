using System;
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
    /// 声明式执行体归属（<c>[Node] fn=</c> 的**显式声明值**；未声明 ⇒ null）。
    /// <para>与 <see cref="FuncName"/> 的区别：<see cref="FuncName"/> 在未声明时缺省回退节点名（保证旧模板可用），
    /// 本属性只承载「声明面真的写了 fn=」这一事实 —— 消费方（表驱动分派）据此判定执行体归属，禁以缺省值推断。</para>
    /// 加法式扩展：缺省 null，旧模板/内置骨架零影响。
    /// </summary>
    public string? Fn { get; init; }

    /// <summary>
    /// 声明式执行体归属（<c>[Node] skill=</c>）：引用 <c>[Skill]</c> 名（会话步经 skill 装配 + 执行端分派）。
    /// 与 <see cref="Fn"/> / <see cref="Llm"/>（<c>llm=</c>）互斥完备（闸A US7802）。
    /// 加法式扩展：缺省 null，旧模板/内置骨架零影响。
    /// </summary>
    public string? Skill { get; init; }

    /// <summary>
    /// 声明式执行体归属（<c>[Node] llm=</c>）：引用 <c>[LLMDecl]</c> 名（对话步契约）。
    /// 与 <see cref="FuncName"/> / <see cref="Skill"/> 互斥完备（闸A US7802）。
    /// 加法式扩展：缺省 null，旧模板/内置骨架零影响。
    /// </summary>
    public string? Llm { get; init; }

    /// <summary>
    /// 声明式执行单元（<c>[Node] executor=</c>）：进程内折叠执行端名（<c>IFoldInProcessExecutor.Name</c>）。
    /// <para>与 <see cref="Fn"/> 分离：<see cref="Fn"/> = 注册工作流函数名（RoundStepInvoker 轴），
    /// 本属性 = 折叠引擎执行端名（SkConversationFoldOperator 轴）。缺省 null ⇒ 解析侧回退 <see cref="Fn"/>。</para>
    /// 加法式扩展：缺省 null，旧模板/内置骨架零影响。
    /// </summary>
    public string? Executor { get; init; }

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

        /// <summary>
        /// 空返回合法性声明（E1）：<c>true</c> = 该步允许返回 null 而不判 <c>exec.function-missing</c>（合法空结果）。
        /// <para>缺省 false = 声明 outport 却返回 null 判缺失（零回归，加法式扩展）。</para>
        /// </summary>
        public bool AllowNullResult { get; init; }

        /// <summary>
        /// 端口级可空声明（E7）：集合内端口允许「显式 null」而不算缺失（键不存在仍算缺失）。
        /// <para>缺省空；<see cref="AllowNullResult"/>=true 时等价于全部结果端口可空（同一开关的两个粒度）。</para>
        /// </summary>
        public IReadOnlyCollection<string>? NullablePorts { get; init; }

        /// <summary>
        /// 条件产出声明：集合内端口允许**缺席**（未触发 = 合法），与 <see cref="NullablePorts"/>（显式 null）区分。
        /// <para>缺省空（零回归，加法式扩展）。</para>
        /// </summary>
        public IReadOnlyCollection<string>? ConditionalPorts { get; init; }

        /// <summary>有效可空端口集（判据唯一来源）：显式端口声明优先，否则按 <see cref="AllowNullResult"/> 取全部结果端口。</summary>
        public IReadOnlyCollection<string> EffectiveNullablePorts
            => NullablePorts ?? (AllowNullResult ? ResultKeys : Array.Empty<string>());

        /// <summary>有效条件产出端口集（判据唯一来源；缺省空）。</summary>
        public IReadOnlyCollection<string> EffectiveConditionalPorts
            => ConditionalPorts ?? Array.Empty<string>();

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
    /// 轮收割姿态（E10）：<c>Container</c> = 轮边界收割到容器（默认，行为不变）；
    /// <c>None</c> = 无容器的一次性声明流，不触发收割（消除每轮 harvestActive=false 告警）。
    /// </summary>
    public enum RoundHarvestPolicy
    {
        /// <summary>收割到容器（默认；与原行为逐字等价）。</summary>
        Container,

        /// <summary>不收割（一次性流：无轮级容器可写）。</summary>
        None,
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

        /// <summary>
        /// 提示词前置声明（I4）：本轮**必填**的提示词端口（如 <c>system</c> / <c>l1</c> / <c>l2</c> / <c>session_input</c>）。
        /// <para>缺省空 ⇒ 前置关闭（零回归）；非空 ⇒ 任何执行体（含 daemon）启动前须经
        /// <see cref="Execution.PromptPreflight.Resolve"/> 判定，缺失即 <c>prompt_missing</c> 并禁止侧通道执行。</para>
        /// </summary>
        public IReadOnlyList<string> RequiredPromptPorts { get; init; } = Array.Empty<string>();

        /// <summary>
        /// 轮收割姿态（E10）：声明驱动，取代运行期探测（有无容器）。
        /// <para>缺省 <see cref="RoundHarvestPolicy.Container"/>（零回归）；一次性声明流置 <see cref="RoundHarvestPolicy.None"/>。</para>
        /// </summary>
        public RoundHarvestPolicy HarvestPolicy { get; init; } = RoundHarvestPolicy.Container;
    }
}
