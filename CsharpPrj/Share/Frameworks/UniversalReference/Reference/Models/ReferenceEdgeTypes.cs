#nullable enable

namespace UniversalWorkflowFramework.Reference.Models
{
    /// <summary>
    /// 引用关系边类型词汇唯一字面量源（L3 框架语义层）。
    /// <para>
    /// B1 收口：原唯一源在 <c>Operation.DataStoreDriven.Abstractions.ReferenceTypes</c>（L1 原语层），
    /// 现上移至框架本体（spec §4.1「框架吸收全部语义」）。DataStoreDriven <c>ReferenceTypes</c> 降为
    /// 别名门面（<c>const = ReferenceEdgeTypes.X</c>），各域既有别名链路不变（间接指向框架）。
    /// </para>
    /// <para>
    /// 端点资源类型（Node/KeyMapping/Task/Session/History/Workflow）保留在 DataStoreDriven
    /// （依赖 <c>NodeTypes</c>，属业务资源标识，非关系语义，不上移）。
    /// </para>
    /// </summary>
    public static class ReferenceEdgeTypes
    {
        // ---- 层级 / 归属边 ----

        /// <summary>task → node 跨库归属边（运行时节点产出归属；真多对多）。</summary>
        public const string CrossDbLink = "cross_db_link";

        /// <summary>会话归属边：session.{name} → 会话消息/产出节点。</summary>
        public const string SessionOwns = "session_owns";

        /// <summary>task → sub-task 归属边（不定深度任务树；同一 sub-task 唯一父）。</summary>
        public const string TaskOwnsTask = "task_owns_task";

        /// <summary>历史归拢任务边：history.{historyId} → task。</summary>
        public const string HistoryOwnsTask = "history_owns_task";

        /// <summary>历史挂载节点边（已降级为派生视图，deprecated 语义：写点退役，仅旧数据兼容读）。</summary>
        public const string HistoryOwnsNode = "history_owns_node";

        // ---- 工作流静态引用边 ----

        /// <summary>控制流：上一步骤调用下一步骤（node → node）。</summary>
        public const string Calls = "Calls";

        /// <summary>组合：工作流包含步骤（workflow → node）。</summary>
        public const string Owns = "Owns";

        /// <summary>跨工作流可见性边界：导出节点对其它工作流可见（workflow → node）。</summary>
        public const string Exports = "Exports";

        /// <summary>工作流模板继承（is-a）：派生工作流 → 基类工作流（workflow → workflow）。</summary>
        public const string Inherits = "Inherits";

        /// <summary>接口实现（对齐 C# interface）：工作流声明实现某端口契约（workflow → contract）。</summary>
        public const string Implements = "Implements";

        /// <summary>编译期依赖（对齐 using）：显式声明上下文依赖（workflow → resource）。</summary>
        public const string DependsOn = "DependsOn";

        // ---- 节点关系编码边 ----

        /// <summary>节点 ↔ 节点数据流组合边（上游输出端口 → 下游输入端口）。</summary>
        public const string NodeToNode = "node_to_node";

        /// <summary>KeyMapping ↔ 节点边（映射属于某节点 / 映射到节点端口）。</summary>
        public const string KeyMappingToNode = "keymapping_to_node";
    }
}
