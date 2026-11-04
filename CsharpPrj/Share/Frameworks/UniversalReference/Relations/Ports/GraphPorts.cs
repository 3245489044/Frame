using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

// port-surface-types: RelationOrigin,RelationOutcome,RelationRequest,RelationScope,RelationResult,RelationQueryResult,
//   IRelationCommandPort,IRelationQueryPort,RelationQueryVocabulary,RelationQuerySpec,IRelationSpecQueryPort,
//   RelationMoveRequest,RelationMergeRequest,RelationGcRequest,IRelationMaintenancePort,
//   RelationGuardPostures,RelationGuardPredicates,RelationGuard,RelationGuardContext,RelationGuardResult,IRelationGuardEvaluator,
//   IRelationPorts,IRelationPortsProvider,IRelationOperationResolver,
//   RelationEdgeSpec,RelationConstraintSpec,IRelationPolicy,IWorkflowNamespace,IEntityExistenceProbe,IRelationDiagnostics,
//   RelationContainerStatus,RelationContainerResolution,IRelationContainerResolver,
//   OutcomeClass,RelationOperation,RelationInvariant,RelationDerivation,RelationPlan,RelationBinding,RelationProvision,
//   RelationViolationKind,RelationViolation,RelationIntent,IRelationGatherPoint,IRelationGatherPointByEdge
//
// 端口面纪律（PS1–PS6，守卫 = Operation\DSL\Node\Check-GraphPortSurface.ps1）：
//   ① 本命名空间的 public 类型只允许出现在本文件（文件外 = 0）；
//   ② 本文件无方法体 / 无 new / 无 IO；计算与工厂一律在 Relations.Kernel 的语义助手；
//   ③ 域词（history/task/node）只作参数值，不进入类型名与受控词。
namespace UniversalWorkflowFramework.Relations;

/// <summary>关系写入来源（裁决可见化：Runtime/Rebuild/Admin）。</summary>
public enum RelationOrigin
{
    Runtime = 0,
    Rebuild = 1,
    Admin = 2,
}

/// <summary>关系操作结果形态（禁以 <c>Ok=true</c> 掩盖未命中/被策略跳过）。</summary>
public enum RelationOutcome
{
    Applied = 0,
    Duplicate = 1,
    NotFound = 2,
    Rejected = 3,
    NotAssembled = 4,
    Skipped = 5,
}

/// <summary>关系写请求（operation 之外的唯一可变面：键 + 作用域 + 来源）。</summary>
public sealed record RelationRequest(
    string SourceKey,
    string? TargetKey = null,
    IReadOnlyList<string>? TargetKeys = null,
    string? Namespace = null,
    string Actor = "system",
    RelationOrigin Origin = RelationOrigin.Runtime,
    string? Operation = null);

/// <summary>关系读范围（起点 + 作用域；<c>Namespace</c> 缺省由 <see cref="IWorkflowNamespace"/> 解析）。</summary>
public sealed record RelationScope(string SourceKey, string? Namespace = null, string? SourceType = null);

/// <summary>关系写结果（结构化诊断；失败不静默）。</summary>
public sealed record RelationResult(
    bool Ok,
    string Operation,
    string? Edge,
    int Written,
    IReadOnlyList<string> Diagnostics,
    RelationOutcome Outcome)
{
    public int Revived { get; init; }

    public IReadOnlyList<string> ReviveReasons { get; init; } = Array.Empty<string>();
}

/// <summary>派生读结果（键集 + 空因；空集必须带原因）。</summary>
public sealed record RelationQueryResult(
    IReadOnlyList<string> Keys,
    IReadOnlyList<string> EmptyReasons);

/// <summary>关系命令端口（<b>唯一写入口</b>）。</summary>
public interface IRelationCommandPort
{
    Task<RelationResult> EnsureAsync(string operation, RelationRequest request, CancellationToken ct = default);

    Task<RelationResult> RemoveAsync(string operation, RelationRequest request, CancellationToken ct = default);

    RelationOperation? Describe(string operation);
}

/// <summary>关系查询端口（<b>唯一读入口</b>）。</summary>
public interface IRelationQueryPort
{
    Task<RelationQueryResult> EnumerateAsync(string edgeOrDerive, RelationScope scope, CancellationToken ct = default);

    Task<RelationQueryResult> DeriveAsync(string deriveId, RelationScope scope, CancellationToken ct = default);
}

/// <summary>关系查询轴取值（单源；禁在查询构造/声明编译/门禁各处手写字面量）。</summary>
public static class RelationQueryVocabulary
{
    /// <summary>归一化 = 原样（身份键）。</summary>
    public const string NormalizeIdentity = "identity";

    /// <summary>归一化 = 节点键口径（跨库链去重用）。</summary>
    public const string NormalizeNodeKey = "node-key";

    /// <summary>排序 = 键序（Ordinal 升序；确定性输出）。</summary>
    public const string OrderKeyOrdinal = "key-ordinal";
}

/// <summary>关系查询规格（F4 契约壳）—— <c>[Derive]</c> 编译产物，声明一次、查询端口统一求值。</summary>
public sealed record RelationQuerySpec(
    string From,
    string? To = null,
    string? Label = null,
    IReadOnlyList<string>? Via = null,
    int MaxDepth = 1,
    int MaxNodes = 0,
    string Normalize = RelationQueryVocabulary.NormalizeIdentity,
    string Order = RelationQueryVocabulary.OrderKeyOrdinal);

/// <summary>规格化关系查询端口（F4 唯一读入口的扩展面）：<see cref="RelationQuerySpec"/> → 键集 + 空因/截断因。</summary>
public interface IRelationSpecQueryPort
{
    Task<RelationQueryResult> QueryAsync(
        RelationQuerySpec spec, RelationScope scope, CancellationToken ct = default);
}

/// <summary>移动请求（<c>move</c>）：把 <c>TargetKey</c> 从原父改挂到新父（detach + attach 原子）。</summary>
public sealed record RelationMoveRequest(
    string Operation,
    string TargetKey,
    string FromSourceKey,
    string ToSourceKey,
    string? Namespace = null,
    string Actor = "system",
    RelationOrigin Origin = RelationOrigin.Runtime);

/// <summary>合并请求（<c>merge</c>）：把一批目标从原父批量改挂到新父（全成全败）。</summary>
public sealed record RelationMergeRequest(
    string Operation,
    IReadOnlyList<string> TargetKeys,
    string FromSourceKey,
    string ToSourceKey,
    string? Namespace = null,
    string Actor = "system",
    RelationOrigin Origin = RelationOrigin.Runtime);

/// <summary>孤儿回收请求（<c>gc</c>）：候选目标必须显式给出（禁全库扫描）。</summary>
public sealed record RelationGcRequest(
    string Operation,
    string SourceKey,
    IReadOnlyList<string> TargetKeys,
    string? Namespace = null,
    string Actor = "system",
    RelationOrigin Origin = RelationOrigin.Rebuild);

/// <summary>维护操作端口（F3）：move / merge / gc 与 ensure/remove 同源。</summary>
public interface IRelationMaintenancePort
{
    Task<RelationResult> MoveAsync(RelationMoveRequest request, CancellationToken ct = default);

    Task<RelationResult> MergeAsync(RelationMergeRequest request, CancellationToken ct = default);

    Task<RelationResult> GcAsync(RelationGcRequest request, CancellationToken ct = default);
}

/// <summary>守卫姿态（结构取值；单源，禁各处手写字面量）。</summary>
public static class RelationGuardPostures
{
    /// <summary>违反即拒绝（fail-closed）。</summary>
    public const string Require = "require";

    /// <summary>违反只可见化（存量降档；不得用于新写路径长期运行）。</summary>
    public const string Reduced = "reduced";

    /// <summary>只记账（等价事后校验；显式降级，禁隐式）。</summary>
    public const string BestEffort = "best-effort";
}

/// <summary>守卫谓词词表（受控词；框架只实现结构事实谓词，域语义一律经声明面填值）。</summary>
public static class RelationGuardPredicates
{
    public const string SourceExists = "source_exists";
    public const string TargetExists = "target_exists";
    public const string TargetSingleParent = "target_single_parent";
    public const string SourceSingleTarget = "source_single_target";
    public const string Acyclic = "acyclic";
    public const string EdgeAbsent = "edge_absent";
    public const string ManyToMany = "many_to_many";
}

/// <summary>
/// 关系守卫（<c>[Invariant]</c> / <c>[Relation] guard.pre|guard.post</c> 的中性运行期形态）。
/// <para>判据载体 = 结构事实（计数 / 存在性 / 环），未登记谓词一律不可判定（不放行）。</para>
/// </summary>
public sealed record RelationGuard(
    string Id,
    string Edge,
    IReadOnlyList<string> Pre,
    IReadOnlyList<string> Post,
    string Posture = RelationGuardPostures.Require,
    string OnViolation = "reject");

/// <summary>守卫求值上下文（调用方在 step 前/后用已有结构事实装配；框架不自行遍历存储）。</summary>
public sealed record RelationGuardContext(
    string SourceKey,
    string TargetKey,
    bool SourceExists = true,
    bool TargetExists = true,
    bool CreatesCycle = false,
    bool EdgeExists = false,
    int ExistingTargetParents = 0,
    int ExistingSourceTargets = 0);

/// <summary>守卫求值结果（违反项逐条可见；不可判定与违反共用出口但原因可辨）。</summary>
public sealed record RelationGuardResult(
    bool Satisfied,
    bool Blocks,
    IReadOnlyList<string> Violations,
    string Posture);

/// <summary>守卫求值端口（纯函数；无 IO / 无状态）。</summary>
public interface IRelationGuardEvaluator
{
    /// <summary>写前求值（<see cref="RelationGuard.Pre"/>）。</summary>
    RelationGuardResult EvaluatePre(RelationGuard guard, RelationGuardContext context);

    /// <summary>写后求值（<see cref="RelationGuard.Post"/>）。</summary>
    RelationGuardResult EvaluatePost(RelationGuard guard, RelationGuardContext context);
}

/// <summary>关系端口聚合（唯一租约）：命令 / 查询 / 声明反查三件同源装配。</summary>
public interface IRelationPorts
{
    IRelationCommandPort Commands { get; }

    IRelationQueryPort Queries { get; }

    IRelationOperationResolver Operations { get; }

    /// <summary>关系聚拢点（唯一求值入口）；未装配 ⇒ null（调用方回退端口直连）。</summary>
    IRelationGatherPoint? Gather { get; }

    /// <summary>维护端口（move/merge/gc 与创建同源）；未装配 ⇒ null（调用方回退逐条写）。</summary>
    IRelationMaintenancePort? Maintenance => null;
}

/// <summary>端口租约提供者：通用编排门面从既有 grounding 句柄取端口（免新增装配点）。</summary>
public interface IRelationPortsProvider
{
    IRelationPorts Ports { get; }
}

/// <summary>关系声明反查（operation / edge+intent / derive）：唯一数据源 = <c>RelationPlanCatalog</c>。</summary>
public interface IRelationOperationResolver
{
    RelationOperation? Resolve(string operation);

    RelationOperation? ResolveByEdge(string edge, bool remove);

    RelationDerivation? ResolveDerivation(string deriveId);

    bool IsDeclaredEdge(string edge);

    bool IsDataflowEdge(string edge);
}

/// <summary>边的中性声明视图（脱离 Plate.DslMethod.Schema 依赖；桥接层负责映射）。</summary>
public sealed record RelationEdgeSpec(
    string Name,
    string Source,
    string Target,
    bool IsDeprecated = false);

/// <summary>约束的中性声明视图。</summary>
public sealed record RelationConstraintSpec(string Edge, string Kind, IReadOnlyList<string>? Values = null);

/// <summary>边策略只读端口（声明驱动；内核不内联 if）。</summary>
public interface IRelationPolicy
{
    RelationEdgeSpec? FindEdge(string edgeName);

    RelationConstraintSpec? FindConstraint(string edgeName);

    bool IsDeclared(string edgeName);

    bool IsDeprecated(string edgeName);
}

/// <summary>工作流命名空间单源端口：所有写边 Location 的唯一取值点。</summary>
public interface IWorkflowNamespace
{
    string Default { get; }

    string Resolve(string? explicitValue, string fallbackKey);
}

/// <summary>端点存在性探针（写前判据；未装配 ⇒ 显式未装配实现）。</summary>
public interface IEntityExistenceProbe
{
    Task<bool> ExistsAsync(string entityType, string id, CancellationToken ct = default);

    bool IsMapped(string entityType);
}

/// <summary>关系诊断出口（结构化；未装配时由内核把原因写进 <see cref="RelationResult.Diagnostics"/>）。</summary>
public interface IRelationDiagnostics
{
    void RelationRejected(string operation, string? edge, string reason);

    void RelationDuplicate(string operation, string? edge, string reason);

    void RelationMissingEndpoint(string operation, string? edge, string endpoint);

    /// <summary>存在性探针未装配（能力缺失，不是「端点不存在」）。</summary>
    void RelationProbeUnassembled(string operation, string? edge, string reason);

    /// <summary>实体类型未映射存在性谓词（声明缺口，不是「端点不存在」）。</summary>
    void RelationProbeUnmapped(string operation, string? edge, string endpoints);

    void RelationInvariantViolation(string operation, string invariant, string reason);

    void RelationPlanMissing(string reason);

    void RelationRevived(string edge, string sourceKey, string targetKey, string? tombstoneReason);

    void RelationWriteSkipped(string operation, string? edge, string verdict, string role, string key);

    /// <summary>判据不适用（合法跳过：写入本身不受影响）。</summary>
    void RelationWriteNotApplicable(string operation, string? edge, string reason, string role, string key);

    /// <summary>判据不可用（未装配 / 无法解析：可见化，不据此拒绝）。</summary>
    void RelationWriteUnavailable(string operation, string? edge, string code, string role, string key);
}

/// <summary>
/// 容器解析状态（三态；治「解析不出」被当成「不一致」而误拒）。
/// <para><c>Resolved</c> = 已取到容器值；<c>NotApplicable</c> = 该端点类型不参与容器归属；
/// <c>Unresolvable</c> = 无法判定（必须可见化，不得据以判 <c>container-mismatch</c>）。</para>
/// </summary>
public enum RelationContainerStatus
{
    Resolved = 0,
    NotApplicable = 1,
    Unresolvable = 2,
}

/// <summary>容器解析结果（唯一判据载体：状态 + 值 + 原因码）。</summary>
public sealed record RelationContainerResolution(
    RelationContainerStatus Status, string? Container, string Reason);

/// <summary>容器归属解析端口（实体行容器键的唯一读取点）。</summary>
public interface IRelationContainerResolver
{
    Task<RelationContainerResolution> ResolveAsync(string entityType, string key, CancellationToken ct = default);
}

/// <summary>关系/挂载结果的中性语义（跨层映射单点；领域枚举保留，本类只做归一）。</summary>
public enum OutcomeClass
{
    Ok,
    Duplicate,
    NotApplicable,
    Rejected,
    Missing,
    Unavailable,
    Partial,
    Failed,
}

/// <summary>关系操作声明（<c>[Relation]</c> 的中性运行期形态；边类型/端点/作用域的唯一来源）。</summary>
public sealed record RelationOperation(
    string Id,
    string? Display,
    string Edge,
    string Source,
    string Target,
    string Flow,
    string Namespace,
    IReadOnlyList<string> Exists,
    string Duplicate,
    string OnMissing,
    string Arity,
    string Intent,
    string? Emit,
    string Container = "",
    string OnMismatch = "",
    string Provision = "");

/// <summary>关系不变量声明（<c>[Invariant]</c> 的中性运行期形态；<c>guard.pre|guard.post</c> 由声明面填充）。</summary>
public sealed record RelationInvariant(
    string Id,
    string Kind,
    string Edge,
    string OnViolation,
    string? Scope,
    IReadOnlyList<string>? Pre = null,
    IReadOnlyList<string>? Post = null,
    string Posture = RelationGuardPostures.Require);

/// <summary>派生成员集声明（<c>[Derive]</c> 的中性运行期形态；<c>Via</c> 序 = 路径序）。</summary>
public sealed record RelationDerivation(
    string Id,
    string From,
    string To,
    IReadOnlyList<string> Via,
    string Op,
    string Normalize,
    string Order,
    IReadOnlyList<string> Empty);

/// <summary>关系计划（Usharp 编译产物 → 中性 DTO；运行期内核唯一输入）。</summary>
public sealed record RelationPlan(
    string Version,
    IReadOnlyList<RelationOperation> Operations,
    IReadOnlyList<RelationInvariant> Invariants,
    IReadOnlyList<RelationDerivation> Derivations,
    IReadOnlyList<RelationBinding>? Bindings = null,
    IReadOnlyList<RelationProvision>? Provisions = null);

/// <summary>容器归属声明（<c>[Binding]</c> 中性形态；U4 起由语言层产出）。</summary>
public sealed record RelationBinding(
    string Id,
    string Target,
    string ContainerFrom,
    string OnMismatch);

/// <summary>端点镜像归属声明（<c>[Provision]</c> 中性形态）。</summary>
public sealed record RelationProvision(
    string Id,
    string Entity,
    string Owner);

/// <summary>违反种类（结构事实；<c>BudgetExceeded</c> 与 <c>Cycle</c> 不得共用出口）。</summary>
public enum RelationViolationKind
{
    Cycle = 0,
    BudgetExceeded = 1,
    SingleParent = 2,
    SingleTarget = 3,
    MissingEndpoint = 4,
}

/// <summary>一条不变量违反（不变量 id + 种类 + 结构事实理由）。</summary>
public sealed record RelationViolation(string InvariantId, RelationViolationKind Kind, string Reason);

/// <summary>聚拢点意图（写侧）：ensure / remove。</summary>
public enum RelationIntent
{
    Ensure = 0,
    Remove = 1,
}

/// <summary>
/// 关系聚拢点（唯一维护面 / 唯一求值入口，中性无域词）：所有写入口只经本端口。
/// <para>段序固定：解析计划 → 端点 provision 委托 → 探针 → 不变量 → duplicate → 唯一写边 → 诊断。</para>
/// </summary>
public interface IRelationGatherPoint
{
    Task<RelationResult> InvokeAsync(RelationIntent intent, RelationRequest request, CancellationToken ct = default);

    Task<RelationResult> InvokeByOperationAsync(
        string operation, RelationIntent intent, RelationRequest request, CancellationToken ct = default);

    Task<RelationQueryResult> DeriveAsync(string deriveId, RelationScope scope, CancellationToken ct = default);

    RelationOperation? Describe(string operation);
}

/// <summary>按边名 + 意图写入（调用点不持 operation 字面量时的唯一入口）。</summary>
public interface IRelationGatherPointByEdge
{
    Task<RelationResult> InvokeByEdgeAsync(
        string edge, RelationIntent intent, RelationRequest request, CancellationToken ct = default);
}
