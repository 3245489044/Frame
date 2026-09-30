namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 引用关系词汇 —— **边类型别名门面 + 端点类型字面量源**（A 线 A1）。
/// <para>
/// B1 收口（2026-09-30）：边类型字面量唯一源上移至框架
/// <see cref="UniversalWorkflowFramework.Reference.Models.ReferenceEdgeTypes"/>（L3 语义层）；
/// 本类 13 个边类型常量降为别名转发（<c>const = ReferenceEdgeTypes.X</c>）。
/// </para>
/// <para>
/// 端点资源类型（Node/KeyMapping/Task/Session/History/Workflow）仍以本类为字面量源
/// （依赖 <c>NodeTypes</c>，属业务资源标识，非关系语义，不上移框架）。
/// </para>
/// <para>
/// 其余常量类（<see cref="CrossDbReferenceTypes"/> / <c>WorkflowReferenceTypes</c> /
/// <c>NodeResourceTypes</c> / <c>Operation.LoopFoldMethod.Reference.Model.ReferenceTypes</c>）
/// 一律别名转发到本类或直接到框架 <see cref="UniversalWorkflowFramework.Reference.Models.ReferenceEdgeTypes"/>。
/// </para>
/// <para>
/// 零依赖层豁免（门禁 <c>Check-ReferenceVocabulary</c> 白名单登记）：<c>Share.ScopeFramework</c>
/// 与 <c>Modules.DeclarativeDataflow</c> 为零项目引用纯逻辑框架，本地字面量按层内自治常量保留。
/// </para>
/// </summary>
public static class ReferenceTypes
{
    // ---- 端点资源类型（ResourceType；字面量源留本层，依赖 NodeTypes）----

    /// <summary>节点资源类型（ur_nodes 业务节点）。</summary>
    public const string Node = NodeTypes.Node;

    /// <summary>键映射资源类型。</summary>
    public const string KeyMapping = NodeTypes.KeyMapping;

    /// <summary>任务资源类型（task 镜像；taskKey 作为图资源 Id）。</summary>
    public const string Task = "task";

    /// <summary>会话根资源类型（会话图子图锚点；Id = "session.{name}"）。</summary>
    public const string Session = "session";

    /// <summary>历史 class 容器资源类型（Id = "history.{historyId}"）。</summary>
    public const string History = "history";

    /// <summary>工作流资源类型（控制流 / 组合边的源端点；Id = workflowId）。</summary>
    public const string Workflow = "workflow";

    // ---- 层级 / 归属边（B1：别名转发到框架 ReferenceEdgeTypes）----

    /// <summary>task → node 跨库归属边（运行时节点产出归属；**真多对多**，同一节点可被多个 task 共享引用）。</summary>
    public const string CrossDbLink = global::UniversalWorkflowFramework.Reference.Models.ReferenceEdgeTypes.CrossDbLink;

    /// <summary>会话归属边：session.{name} → 会话消息/产出节点（会话图子图挂载，读侧按 session 过滤）。</summary>
    public const string SessionOwns = global::UniversalWorkflowFramework.Reference.Models.ReferenceEdgeTypes.SessionOwns;

    /// <summary>task → sub-task 归属边（**不定深度任务树**；同一 sub-task 唯一父）。</summary>
    public const string TaskOwnsTask = global::UniversalWorkflowFramework.Reference.Models.ReferenceEdgeTypes.TaskOwnsTask;

    /// <summary>历史归拢任务边：history.{historyId} → task（一个历史归拢多个任务）。</summary>
    public const string HistoryOwnsTask = global::UniversalWorkflowFramework.Reference.Models.ReferenceEdgeTypes.HistoryOwnsTask;

    /// <summary>历史挂载节点边（**已降级为派生视图**，deprecated 语义：写点已退役，保留仅作旧数据兼容读）。</summary>
    public const string HistoryOwnsNode = global::UniversalWorkflowFramework.Reference.Models.ReferenceEdgeTypes.HistoryOwnsNode;

    // ---- 工作流静态引用边（B1：别名转发到框架 ReferenceEdgeTypes）----

    /// <summary>控制流：上一步骤调用下一步骤（node → node，按 RoundSpec 主路径线性序）。</summary>
    public const string Calls = global::UniversalWorkflowFramework.Reference.Models.ReferenceEdgeTypes.Calls;

    /// <summary>组合：工作流包含步骤（workflow → node）。</summary>
    public const string Owns = global::UniversalWorkflowFramework.Reference.Models.ReferenceEdgeTypes.Owns;

    /// <summary>跨工作流可见性边界：导出节点对其它工作流可见（workflow → node）。</summary>
    public const string Exports = global::UniversalWorkflowFramework.Reference.Models.ReferenceEdgeTypes.Exports;

    /// <summary>工作流模板继承（对齐类继承 is-a）：派生工作流 → 基类工作流（workflow → workflow）。</summary>
    public const string Inherits = global::UniversalWorkflowFramework.Reference.Models.ReferenceEdgeTypes.Inherits;

    /// <summary>接口实现（对齐 C# interface）：工作流声明实现某端口契约（workflow → contract）。</summary>
    public const string Implements = global::UniversalWorkflowFramework.Reference.Models.ReferenceEdgeTypes.Implements;

    /// <summary>编译期依赖（对齐 using）：显式声明上下文依赖（workflow → resource）。</summary>
    public const string DependsOn = global::UniversalWorkflowFramework.Reference.Models.ReferenceEdgeTypes.DependsOn;

    // ---- 节点关系编码边（refType 前缀；编解码唯一实现见 NodeRelationRefCodec）----

    /// <summary>节点 ↔ 节点数据流组合边（上游输出端口 → 下游输入端口）。</summary>
    public const string NodeToNode = global::UniversalWorkflowFramework.Reference.Models.ReferenceEdgeTypes.NodeToNode;

    /// <summary>KeyMapping ↔ 节点边（映射属于某节点 / 映射到节点端口）。</summary>
    public const string KeyMappingToNode = global::UniversalWorkflowFramework.Reference.Models.ReferenceEdgeTypes.KeyMappingToNode;
}
