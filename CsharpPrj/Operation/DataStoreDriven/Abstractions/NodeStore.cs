using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using Plate.DslMethod.Usharp;

namespace Operation.DataStoreDriven.Abstractions;

/// <summary>
/// 标准化节点数据记录（ur_nodes）。统一承载输入/输出/事件/检查点/资源等任意数据，
/// 以 <see cref="NodeType"/> 判别语义（不改变存储形态）。对齐「三种数据」模型的第一种——标准化节点数据。
/// </summary>
public sealed class NodeRecord
{
    private const string FieldNodeType = "nodeType";
    private const string FieldThreadId = "threadId";
    private const string FieldKey = "key";
    private const string FieldStatus = "status";
    private const string FieldStepKey = "stepKey";
    private const string FieldDecision = "decision";
    private const string FieldPayload = "payload";
    private const string FieldRelations = "relations";
    private const string FieldCreatedAt = "createdAt";
    private const string FieldUpdatedAt = "updatedAt";

    private readonly UsharpRecord _record;

    /// <summary>空视图（兼容对象初始化器消费方）。</summary>
    public NodeRecord() : this(new UsharpRecord(Array.Empty<KeyValuePair<string, UsharpValue>>())) { }

    /// <summary>薄视图构造：直接持有中立体 <see cref="UsharpRecord"/>（**同一实例**读写，非拷贝）。</summary>
    public NodeRecord(UsharpRecord record)
        => _record = record ?? throw new ArgumentNullException(nameof(record));

    /// <summary>中立体后盾（视图单源；字段经 <see cref="UsharpRecord.TryGet"/>/<c>Set</c> 直读写同一实例）。</summary>
    public UsharpRecord Record => _record;

    public long Id { get; set; }

    /// <summary>节点唯一键（幂等 upsert 依据；非记录体字段，独立承载）。</summary>
    public string NodeKey { get; set; } = string.Empty;

    /// <summary>节点类型判别（<see cref="NodeTypes"/>，如 input/output/event/checkpoint/resource）。</summary>
    public string NodeType
    {
        get => Str(FieldNodeType) ?? NodeTypes.Node;
        set => SetStr(FieldNodeType, value);
    }

    /// <summary>所属线程/会话关联键（可选，便于按 thread 过滤）。</summary>
    public string? ThreadId { get => Str(FieldThreadId); set => SetStr(FieldThreadId, value); }

    /// <summary>业务键（通用辅助列）：三区收敛后的 InputKey / EventKey 等业务关联键（可选）。</summary>
    public string? Key { get => Str(FieldKey); set => SetStr(FieldKey, value); }

    /// <summary>状态/子类型辅助列（可选）：Output 状态 / Event 子类型等。</summary>
    public string? Status { get => Str(FieldStatus); set => SetStr(FieldStatus, value); }

    /// <summary>驱动标识符 D（可选）：本步节点键（重放序列的推进键，对齐 FoldSignal.StepKey）。</summary>
    public string? StepKey { get => Str(FieldStepKey); set => SetStr(FieldStepKey, value); }

    /// <summary>决策标识符 C（可选）：规范化决策词 continue/stop/error（对齐 AX.DecisionOutputSet）。</summary>
    public string? Decision { get => Str(FieldDecision); set => SetStr(FieldDecision, value); }

    /// <summary>节点载荷（**中立 USharp 值**：记录/列表/标量）。路线 B 数据内核原生表示——存储真源唯一形态。</summary>
    public UsharpValue? Payload { get => Val(FieldPayload); set => SetVal(FieldPayload, value); }

    /// <summary>[已退役·G4] 节点关系状态（**中立 USharp 值**）。关系权威已归属引用库（D3/D4）。
    /// 仅旧数据读兼容，写侧逐步不再产出。</summary>
    public UsharpValue? Relations { get => Val(FieldRelations); set => SetVal(FieldRelations, value); }

    /// <summary>首次写入时间（UTC ISO-8601，随记录体持久化；幂等覆写保留）。</summary>
    public string? CreatedAt { get => Str(FieldCreatedAt); set => SetStr(FieldCreatedAt, value); }

    /// <summary>最近写入时间（UTC ISO-8601，随记录体持久化）。</summary>
    public string? UpdatedAt { get => Str(FieldUpdatedAt); set => SetStr(FieldUpdatedAt, value); }

    /// <summary>读标量文本（缺省/null ⇒ null）。</summary>
    private string? Str(string name)
        => _record.TryGet(name, out var v) && v is UsharpScalar sc && sc.Kind != UsharpScalarKind.Null ? sc.Text : null;

    /// <summary>写标量文本（空白 ⇒ 删除字段，与「null/空白字段不写出」一致）。</summary>
    private void SetStr(string name, string? value)
    {
        if (string.IsNullOrWhiteSpace(value)) _record.Remove(name);
        else _record.Set(name, UsharpScalar.Str(value));
    }

    /// <summary>读中立值（缺省/null ⇒ null）。</summary>
    private UsharpValue? Val(string name)
        => _record.TryGet(name, out var v) && v is not UsharpScalar { Kind: UsharpScalarKind.Null } ? v : null;

    /// <summary>写中立值（null ⇒ 删除字段）。</summary>
    private void SetVal(string name, UsharpValue? value)
    {
        if (value is null) _record.Remove(name);
        else _record.Set(name, value);
    }
}

/// <summary>
/// 节点类型判别常量（仅作语义标记，不改变化一存储形态）。
/// 「基础节点」承载一切逻辑/记录：输入、输出、事件、检查点(记录点)、配置、KeyMapping、资源。
/// 三库收敛目标：config / keymapping / checkpoint 均为 <see cref="NodeTypes"/> 判别节点，并入统一节点库。
/// </summary>
public static class NodeTypes
{
    public const string Node = "node";
    public const string Input = "input";
    public const string Output = "output";
    public const string Event = "event";
    public const string Checkpoint = "checkpoint";
    public const string Resource = "resource";

    /// <summary>配置即数据行（并入节点库：NodeType=config，Key=配置键，PayloadJson=值）。</summary>
    public const string Config = "config";

    /// <summary>KeyMapping 逻辑（并入节点库：NodeType=keymapping，Key=&lt;category&gt;.&lt;mappingKey&gt;，PayloadJson=映射 JSON）。</summary>
    public const string KeyMapping = "keymapping";

    /// <summary>声明关系节点（并入节点库：NodeType=relation，NodeKey="relations.{workflowId}.{relationId}"，PayloadJson=声明记录）。
    /// 主动声明的关系（预声明组合 / keymapping 逻辑绑定），权威在 SQL，图库由守卫经声明记录推导边（G3 声明式构建）。</summary>
    public const string Relation = "relation";

    /// <summary>子 Cline 会话消息归档节点（并入节点库：NodeType=cline_messages，NodeKey="cline.session.{sessionId}.round.{round}.{kind}.{ordinal}"，
    /// ThreadId=sessionId，PayloadJson=messages.json 全文）。每轮一条 NodeKey（幂等 upsert），
    /// 与主程序 ConversationLog（ur_tasks.__conversation）同库可跨库联查，支撑子 Cline 对话历史节点化落库。</summary>
    public const string ClineMessages = "cline_messages";

    /// <summary>会话根节点（方案 C：会话 = LiteDB 引用关系连接的节点子图，NodeKey="session.{name}"，NodeType=session）。
    /// 承载会话状态真源（NextRound / 工作流引用），取代「ur_tasks 行 + __conversation blob」作为会话锚点；
    /// 「新建/刷新/载入历史」均退化为对引用图的操作（写根/查根/投影子图）。</summary>
    public const string Session = "session";

    /// <summary>会话消息节点（NodeKey=<c>session.{name}.r{round}.{kind}.{ordinal}</c>（键权威 = SessionMessageKeys），
    /// NodeType=conversation_message，PayloadJson=单条 ConversationMessage JSON；
    /// <c>ordinal</c> 为**轮内全局到达序**，历史顺序由键内 <c>(round, ordinal)</c> 决定 —— 轮次段使跨轮消息各占独立键，不被 upsert 覆盖）。</summary>
    public const string ConversationMessage = "conversation_message";

    /// <summary>过程节点的**读兼容**判别值（历史行）：运行域事实（阶段 / 端点心跳 / LLM 调用 / 工具；
    /// NodeKey=<c>proc.{container}.r{round}.{kind}.{ordinal}</c>，ThreadId=container）。
    /// 「基础节点化」后写侧恒产 <see cref="Event"/>（过程逻辑升格为事件类基础节点，可被节点库通用寻址/聚拢）；
    /// 本判别值仅为既有落库行读兼容，由过程域载体以「兼容判别值」传入枚举（写入不再产出）。</summary>
    public const string Process = "process";

    /// <summary>全部逻辑/记录节点类型（三库收敛后由统一节点库承载的判别集）。
    /// 本集合是**写路径白名单**（UI/MCP 校验 nodeType 合法性），新增判别值必须同步追加，否则写路径直接拒写。</summary>
    public static readonly IReadOnlyCollection<string> LogicRecordTypes =
        new[] { Input, Output, Event, Checkpoint, Config, KeyMapping, Resource, Node, Relation, ClineMessages, Session, ConversationMessage, Process };
}

/// <summary>
/// 标准化节点数据端口（ur_nodes）：任意输入/输出/事件/检查点/资源统一为节点，<see cref="NodeRecord.NodeKey"/> 幂等。
/// 替代「三区（input/output/event）各自一张表」的语义区分，收敛为单一节点表。
/// </summary>
public interface INodeStore
{
    Task InitializeAsync(CancellationToken ct = default);

    /// <summary>写入/更新节点（NodeKey 幂等 upsert），返回自增 Id。</summary>
    Task<long> PutAsync(NodeRecord node, CancellationToken ct = default);

    Task<NodeRecord?> GetAsync(string nodeKey, CancellationToken ct = default);

    /// <summary>分页列出节点；可按 NodeType / ThreadId 过滤，afterId 增量分页。</summary>
    Task<IReadOnlyList<NodeRecord>> ListAsync(string? nodeType = null, string? threadId = null,
        long? afterId = null, int limit = 100, CancellationToken ct = default);

    Task<bool> DeleteAsync(string nodeKey, CancellationToken ct = default);

    /// <summary>批量删除节点（NodeKey 集合，幂等：不存在的键跳过），返回实际删除的键。</summary>
    Task<IReadOnlyList<string>> DeleteManyAsync(IEnumerable<string> nodeKeys, CancellationToken ct = default);
}
