using Operation.DataStoreDriven.Abstractions;

namespace Operation.LoopFoldMethod.Reference.Model;

/// <summary>
/// 引用关系边类型常量（Reference 抽象的唯一 vocab 源）—— 收敛设计《LoopFoldMethod·Reference 层级关系整理设计》§二/§三：
/// 把散落在 HierarchyMethod 四处的层级与引用边类型（History→Task→Node + 各类边）收敛为单一中立面。
/// Reference 只描述「实体与实体之间存在哪种引用关系」；写边原语仍留 DataStoreDriven（TH-12），本类不承载写边逻辑。
/// <para>
/// 单一来源纪律：凡 DataStoreDriven 已具权威常量的（history_owns_task / session_owns / round_sequence /
/// cross_db_link / Owns / Calls / Exports），本类以 const 直接引用其值，不重复定义字符串；
/// <see cref="NodeToNode"/> / <see cref="KeyMappingToNode"/> 因图协调器 <c>ReferenceGraphCoordinator.Ref</c> 为
/// 私有常量、无公开源，此处声明为 Reference 层规范字符串并标注对齐（稳定图边契约，发散风险极低）。
/// </para>
/// </summary>
public static class ReferenceTypes
{
    /// <summary>历史归拢任务边：history.{historyId} → task（History=Task 上层 class 容器）。</summary>
    public const string HistoryOwnsTask = CrossDbReferenceTypes.HistoryOwnsTask;

    /// <summary>会话归属边：session.{name} → 会话消息/产出节点（会话图子图挂载）。</summary>
    public const string SessionOwns = CrossDbReferenceTypes.SessionOwns;

    /// <summary>会话/轮次时序边：节点 → 下一节点（round 时序链，历史顺序由引用边决定）。</summary>
    public const string RoundSequence = CrossDbReferenceTypes.RoundSequence;

    /// <summary>跨库归属边：ur_tasks.{taskKey} → ur_nodes.{wf.node}（运行时节点产出归属）。</summary>
    public const string CrossDbLink = CrossDbReferenceTypes.CrossDbLink;

    /// <summary>组合边：工作流包含步骤（workflow → node）。</summary>
    public const string Owns = WorkflowReferenceTypes.Owns;

    /// <summary>控制流边：上一步骤调用下一步骤（node → node，按主路径线性序）。</summary>
    public const string Calls = WorkflowReferenceTypes.Calls;

    /// <summary>跨工作流可见性边界（对齐 public 成员）：导出节点对其它工作流可见（workflow → node）。</summary>
    public const string Exports = WorkflowReferenceTypes.Exports;

    /// <summary>节点 ↔ 节点数据流组合边（上游输出端口 → 下游输入端口）。对齐图协调器 <c>Ref.NodeToNode</c>。</summary>
    public const string NodeToNode = "node_to_node";

    /// <summary>KeyMapping ↔ 节点边（映射属于某节点 / 映射到节点端口）。对齐图协调器 <c>Ref.KeyMappingToNode</c>。</summary>
    public const string KeyMappingToNode = "keymapping_to_node";
}
