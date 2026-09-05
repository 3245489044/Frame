namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 引用关系类型常量（唯一权威）：关系唯一权威在 LiteDB 引用边，task 作用域寻址经 task→node 归属边承载。
/// <c>cross_db_link</c>：运行时节点产出后由守卫 <c>ReferenceGrounding.EnsureTaskNodeLinkAsync</c>
/// 建立的「ur_tasks.{taskKey} → ur_nodes.{wf.node}」归属边（幂等加法，不并入数据流权威边集），
/// 供读侧（task 作用域寻址）按 task 过滤节点、根除跨 task 污染。
/// </summary>
public static class CrossDbReferenceTypes
{
    /// <summary>task → node 跨库归属边（运行时节点产出归属）。**真多对多**：一条 (task,workflowId,nodeKey) 一条边，
    /// 同一节点可被多个 task 共享引用（LinkTaskNodeAsync / UnlinkTaskNodeAsync）。</summary>
    public const string CrossDbLink = "cross_db_link";

    /// <summary>会话归属边（方案 C）：session.{name} → 会话消息/产出节点（会话图子图挂载，读侧按 session 过滤节点）。
    /// 与 <see cref="CrossDbLink"/>（ur_tasks → ur_nodes）正交：本边以会话根为锚，不依赖任务池行。</summary>
    public const string SessionOwns = "session_owns";

    /// <summary>会话时序边（方案 C）：消息节点 → 下一消息节点（round 时序链；会话历史顺序由 LiteDB 引用边决定，
    /// 每节点 seq = 其在链上的序号）。</summary>
    public const string RoundSequence = "round_sequence";

    /// <summary>历史归拢任务边：history.{historyId} → task（taskKey）。History=Task 更上层的 class 容器，
    /// 一个历史归拢多个任务；成员集由本出边集合推导（图即真源，可 graph.rebuild 幂等重建）。</summary>
    public const string HistoryOwnsTask = "history_owns_task";

    /// <summary>历史挂载节点边：history.{historyId} → wf.node。**已降级为派生视图**（deprecated 语义，不再独立存边）：
    /// 历史节点集 = 其全部归拢任务的 task→node（cross_db_link）边并集，由
    /// <see cref="IHierarchyReferenceManager.GetHistoryNodeKeysAsync(string,string,System.Threading.CancellationToken)"/> 派生。
    /// 保留常量仅作旧数据兼容；新写路径不再产出本边。</summary>
    public const string HistoryOwnsNode = "history_owns_node";
}

/// <summary>
/// 投影候选清单项（projection 层，对齐 MCP「复合节点引用关系」）：给定 task，沿 LiteDB 引用边
/// （cross_db_link 挂载 + NodeToNode 组合）投影出的候选节点**索引**（不携带载荷——载荷由 selection 层按需加载）。
/// <see cref="Order"/> 为沿引用边推导的顺序（上游先于下游，体现「节点顺序由 LiteDB 引用边决定」）。
/// </summary>
public sealed record NodeProjectionItem(
    string NodeKey,
    string WorkflowId,
    int Order);

