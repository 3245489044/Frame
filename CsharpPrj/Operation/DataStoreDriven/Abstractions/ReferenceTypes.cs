namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 引用关系词汇 —— **边类型 + 端点类型的唯一字面量源**（A 线 A1）。
/// <para>
/// 全仓（零依赖层豁免项除外）所有边类型 / 端点类型字符串只在本类**字面书写**；其余常量类
/// （<see cref="CrossDbReferenceTypes"/> / <see cref="WorkflowReferenceTypes"/> /
/// <see cref="NodeResourceTypes"/> / <c>Operation.LoopFoldMethod.Reference.Model.ReferenceTypes</c>）
/// 一律**别名转发**（<c>const X = ReferenceTypes.X;</c>），不再各自写字面量。
/// </para>
/// <para>
/// 落点理由（与《引用关系模块化-工作流与UR引入及组装归流收尾-综合方案》§2.3-A1 的差异）：方案原文拟把唯一源
/// 置于抽象层 <c>Operation.LoopFoldMethod.Reference</c>，但原语层（本层）与上层的依赖方向恒单向
/// （LoopFoldMethod → DataStoreDriven，见 LoopFoldMethod.csproj 的「脊柱依赖方向恒单向」ModuleRule_Rule），
/// 由本层别名转发到上层即构成**工程环** ⇒ 唯一源只能落在最低可共用层。抽象层
/// <c>LoopFoldMethod.Reference.Model.ReferenceTypes</c> 保留为**向上别名门面**，不承载字面量。
/// </para>
/// <para>
/// 零依赖层豁免（门禁 <c>Check-ReferenceVocabulary</c> 白名单登记，非第二词表）：<c>Share.ScopeFramework</c>
/// （Layer 3）与 <c>Modules.DeclarativeDataflow</c>（Layer 2）为**零项目引用**的纯逻辑框架，物理上无法引用本类
/// ⇒ 其本地字面量按「层内自治常量」保留。
/// </para>
/// </summary>
public static class ReferenceTypes
{
    // ---- 端点资源类型（ResourceType）----

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

    // ---- 层级 / 归属边 ----

    /// <summary>task → node 跨库归属边（运行时节点产出归属；**真多对多**，同一节点可被多个 task 共享引用）。</summary>
    public const string CrossDbLink = "cross_db_link";

    /// <summary>会话归属边：session.{name} → 会话消息/产出节点（会话图子图挂载，读侧按 session 过滤）。</summary>
    public const string SessionOwns = "session_owns";

    /// <summary>task → sub-task 归属边（**不定深度任务树**；同一 sub-task 唯一父）。</summary>
    public const string TaskOwnsTask = "task_owns_task";

    /// <summary>历史归拢任务边：history.{historyId} → task（一个历史归拢多个任务）。</summary>
    public const string HistoryOwnsTask = "history_owns_task";

    /// <summary>历史挂载节点边（**已降级为派生视图**，deprecated 语义：写点已退役，保留仅作旧数据兼容读）。</summary>
    public const string HistoryOwnsNode = "history_owns_node";

    // ---- 工作流静态引用边 ----

    /// <summary>控制流：上一步骤调用下一步骤（node → node，按 RoundSpec 主路径线性序）。</summary>
    public const string Calls = "Calls";

    /// <summary>组合：工作流包含步骤（workflow → node）。</summary>
    public const string Owns = "Owns";

    /// <summary>跨工作流可见性边界：导出节点对其它工作流可见（workflow → node）。</summary>
    public const string Exports = "Exports";

    /// <summary>工作流模板继承（对齐类继承 is-a）：派生工作流 → 基类工作流（workflow → workflow）。</summary>
    public const string Inherits = "Inherits";

    /// <summary>接口实现（对齐 C# interface）：工作流声明实现某端口契约（workflow → contract）。</summary>
    public const string Implements = "Implements";

    /// <summary>编译期依赖（对齐 using）：显式声明上下文依赖（workflow → resource）。</summary>
    public const string DependsOn = "DependsOn";

    // ---- 节点关系编码边（refType 前缀；编解码唯一实现见 NodeRelationRefCodec）----

    /// <summary>节点 ↔ 节点数据流组合边（上游输出端口 → 下游输入端口）。</summary>
    public const string NodeToNode = "node_to_node";

    /// <summary>KeyMapping ↔ 节点边（映射属于某节点 / 映射到节点端口）。</summary>
    public const string KeyMappingToNode = "keymapping_to_node";
}
