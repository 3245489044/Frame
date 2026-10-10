namespace Modules.Node.Relations;

/// <summary>
/// 引用关系词汇 —— 边类型 / 端点资源类型的**字面量唯一源**（B1 二批：自
/// <c>Operation.DataStoreDriven.Abstractions.ReferenceTypes</c>（L4）上移至 L2 通用节点框架；
/// 与后端无关、纯 BCL，节点关系语义随 <see cref="NodeRelation"/> 单源在框架）。
/// <para>
/// 上层 <c>Operation.DataStoreDriven.Abstractions.ReferenceTypes</c> 等常量类一律降为别名转发
/// （<c>const = ReferenceTypes.X</c>），禁第二处字面量。
/// </para>
/// <para>
/// 零依赖层豁免（门禁 <c>Check-ReferenceVocabulary</c> 白名单登记）：<c>Share.ScopeFramework</c>
/// 与 <c>Modules.DeclarativeDataflow</c> 为零项目引用纯逻辑框架，本地字面量按层内自治常量保留。
/// </para>
/// </summary>
public static class ReferenceTypes
{
    // ---- 端点资源类型（ResourceType）----

    /// <summary>节点资源类型（ur_nodes 业务节点）。</summary>
    public const string Node = "node";

    /// <summary>键映射资源类型。</summary>
    public const string KeyMapping = "keymapping";

    /// <summary>任务资源类型（task 镜像；taskKey 作为图资源 Id）。</summary>
    public const string Task = "task";

    /// <summary>会话根资源类型（会话图子图锚点；Id = "session.{name}"）。</summary>
    public const string Session = "session";

    /// <summary>历史 class 容器资源类型（Id = "history.{historyId}"）。</summary>
    public const string History = "history";

    /// <summary>工作流资源类型（控制流 / 组合边的源端点；Id = workflowId）。</summary>
    public const string Workflow = "workflow";

    // ---- 工作流静态引用边（对齐 DSL <c>[Edge] Calls</c> / <c>[Edge] Owns</c>）----

    /// <summary>控制流：上一步骤调用下一步骤（node → node，按 RoundSpec 主路径线性序）。</summary>
    public const string Calls = "Calls";

    /// <summary>组合：工作流包含步骤（workflow → node）。</summary>
    public const string Owns = "Owns";

    /// <summary>跨工作流可见性边界（对齐 public 成员）：导出节点对其它工作流可见（workflow → node）。</summary>
    public const string Exports = "Exports";

    /// <summary>工作流模板继承（对齐类继承 is-a）：派生工作流 → 基类工作流（workflow → workflow）。</summary>
    public const string Inherits = "Inherits";

    /// <summary>接口实现（对齐 C# interface）：工作流声明实现某端口契约（workflow → contract）。</summary>
    public const string Implements = "Implements";

    /// <summary>编译期依赖（对齐 using）：显式声明上下文依赖（workflow → resource）。</summary>
    public const string DependsOn = "DependsOn";

    // ---- 节点关系编码边（refType 前缀；编解码唯一实现留 L4 NodeRelationRefCodec）----

    /// <summary>节点 ↔ 节点数据流组合边（上游输出端口 → 下游输入端口）。</summary>
    public const string NodeToNode = "node_to_node";

    /// <summary>KeyMapping ↔ 节点边（映射属于某节点 / 映射到节点端口）。</summary>
    public const string KeyMappingToNode = "keymapping_to_node";
}
