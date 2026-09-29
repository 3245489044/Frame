namespace Operation.LoopFoldMethod.Reference.Model;

/// <summary>
/// 引用关系边类型常量（**抽象层别名门面**，A1）—— 本类不再字面书写任何边类型：
/// 字面量唯一源 = <c>Operation.DataStoreDriven.Abstractions.ReferenceTypes</c>（原语层，随写边 owner 同层）。
/// <para>
/// 为何唯一源不在本层：《引用关系模块化-工作流与UR引入及组装归流收尾-综合方案》§2.3-A1 拟把唯一源置于本抽象层，
/// 但层次依赖恒单向 <c>LoopFoldMethod → DataStoreDriven</c>（见 LoopFoldMethod.csproj 的
/// 「脊柱依赖方向恒单向」ModuleRule_Rule）⇒ 原语层（DataStoreDriven）别名转发到本层即**工程环**；
/// 故本类退化为**向上别名**，语义边界不变（Reference 只描述「实体之间存在哪种引用关系」，写边原语仍留 DataStoreDriven，TH-12）。
/// </para>
/// <para>
/// 单一来源纪律：本类一律 <c>const = 唯一源.X</c>，不重复定义字符串；新增边类型只改唯一源一处。
/// 端点类型（node / keymapping / task / session / history / workflow）同样以唯一源为准。
/// </para>
/// </summary>
public static class ReferenceTypes
{
    /// <summary>历史归拢任务边：history.{historyId} → task（History=Task 上层 class 容器）。</summary>
    public const string HistoryOwnsTask = global::Operation.DataStoreDriven.Abstractions.ReferenceTypes.HistoryOwnsTask;

    /// <summary>会话归属边：session.{name} → 会话消息/产出节点（会话图子图挂载）。</summary>
    public const string SessionOwns = global::Operation.DataStoreDriven.Abstractions.ReferenceTypes.SessionOwns;

    /// <summary>跨库归属边：ur_tasks.{taskKey} → ur_nodes.{wf.node}（运行时节点产出归属）。</summary>
    public const string CrossDbLink = global::Operation.DataStoreDriven.Abstractions.ReferenceTypes.CrossDbLink;

    /// <summary>组合边：工作流包含步骤（workflow → node）。</summary>
    public const string Owns = global::Operation.DataStoreDriven.Abstractions.ReferenceTypes.Owns;

    /// <summary>控制流边：上一步骤调用下一步骤（node → node，按主路径线性序）。</summary>
    public const string Calls = global::Operation.DataStoreDriven.Abstractions.ReferenceTypes.Calls;

    /// <summary>跨工作流可见性边界（对齐 public 成员）：导出节点对其它工作流可见（workflow → node）。</summary>
    public const string Exports = global::Operation.DataStoreDriven.Abstractions.ReferenceTypes.Exports;

    /// <summary>节点 ↔ 节点数据流组合边（上游输出端口 → 下游输入端口）。</summary>
    public const string NodeToNode = global::Operation.DataStoreDriven.Abstractions.ReferenceTypes.NodeToNode;

    /// <summary>KeyMapping ↔ 节点边（映射属于某节点 / 映射到节点端口）。</summary>
    public const string KeyMappingToNode = global::Operation.DataStoreDriven.Abstractions.ReferenceTypes.KeyMappingToNode;
}
