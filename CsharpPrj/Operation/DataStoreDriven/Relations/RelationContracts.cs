using Plate.DslMethod.Schema;

namespace Operation.DataStoreDriven.Relations;

/// <summary>关系写入来源（裁决可见化：Runtime/Rebuild/Admin）。</summary>
public enum RelationOrigin
{
    Runtime = 0,
    Rebuild = 1,
    Admin = 2,
}

/// <summary>关系操作结果形态（禁以 <c>Ok=true</c> 掩盖未命中）。</summary>
public enum RelationOutcome
{
    Applied = 0,
    Duplicate = 1,
    NotFound = 2,
    Rejected = 3,
    NotAssembled = 4,
}

/// <summary>关系写请求（operation 之外的唯一可变面：键 + 作用域 + 来源）。</summary>
public sealed record RelationRequest(
    string SourceKey,
    string? TargetKey = null,
    IReadOnlyList<string>? TargetKeys = null,
    string? Namespace = null,
    string Actor = "system",
    RelationOrigin Origin = RelationOrigin.Runtime)
{
    /// <summary>目标键集（<c>arity=one</c> 用 <see cref="TargetKey"/>；<c>many</c> 用 <see cref="TargetKeys"/>）。</summary>
    public IReadOnlyList<string> Targets
        => TargetKeys is { Count: > 0 } many
            ? many
            : TargetKey is { Length: > 0 } one ? new[] { one } : Array.Empty<string>();
}

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
    public static RelationResult Rejected(string operation, string? edge, string reason)
        => new(false, operation, edge, 0, new[] { reason }, RelationOutcome.Rejected);
}

/// <summary>派生读结果（键集 + 空因；空集必须带原因）。</summary>
public sealed record RelationQueryResult(
    IReadOnlyList<string> Keys,
    IReadOnlyList<string> EmptyReasons)
{
    public static RelationQueryResult Empty(string reason) => new(Array.Empty<string>(), new[] { reason });
}

/// <summary>
/// 关系命令端口（<b>唯一写入口</b>）：Ensure/Remove 共用同一 operation 声明。
/// </summary>
public interface IRelationCommandPort
{
    Task<RelationResult> EnsureAsync(string operation, RelationRequest request, CancellationToken ct = default);

    Task<RelationResult> RemoveAsync(string operation, RelationRequest request, CancellationToken ct = default);

    /// <summary>只读声明视图（operation → 声明事实；未声明 ⇒ null）。删除意图只由声明面 <c>intent=</c> 决定。</summary>
    RelationOperation? Describe(string operation);
}

/// <summary>关系查询端口（<b>唯一读入口</b>）：按边枚举 / 按派生集求值。</summary>
public interface IRelationQueryPort
{
    Task<RelationQueryResult> EnumerateAsync(string edgeOrDerive, RelationScope scope, CancellationToken ct = default);

    Task<RelationQueryResult> DeriveAsync(string deriveId, RelationScope scope, CancellationToken ct = default);
}

/// <summary>边策略只读端口（声明驱动；内核不内联 if）。</summary>
public interface IRelationPolicy
{
    ReferenceEdgeType? FindEdge(string edgeName);

    ReferenceConstraint? FindConstraint(string edgeName);

    bool IsDeclared(string edgeName);

    bool IsDeprecated(string edgeName);
}

/// <summary>未装配策略（plan 编译失败时的显式占位）：一切边均视为未声明/未废弃。</summary>
public sealed class UnassembledRelationPolicy : IRelationPolicy
{
    public static readonly UnassembledRelationPolicy Instance = new();

    public ReferenceEdgeType? FindEdge(string edgeName) => null;

    public ReferenceConstraint? FindConstraint(string edgeName) => null;

    public bool IsDeclared(string edgeName) => false;

    public bool IsDeprecated(string edgeName) => false;
}

/// <summary>
/// 工作流命名空间单源端口：所有写边 Location 的唯一取值点。
/// <para>禁调用方各写 <c>threadId ?? "agent-round"</c> / <c>string.Empty</c> / 本地归一。</para>
/// </summary>
public interface IWorkflowNamespace
{
    /// <summary>声明面默认作用域（keyspace 的 <c>hierarchy.node/Endpoint</c> 解析值）。</summary>
    string Default { get; }

    /// <summary>解析作用域：显式值优先，缺失走默认；<paramref name="fallbackKey"/> 仅用于诊断归因。</summary>
    string Resolve(string? explicitValue, string fallbackKey);
}

/// <summary>端点存在性探针（写前判据；未装配 ⇒ <see cref="UnassembledEntityExistenceProbe"/>）。</summary>
public interface IEntityExistenceProbe
{
    Task<bool> ExistsAsync(string entityType, string id, CancellationToken ct = default);

    /// <summary>
    /// 该实体类型是否已映射到存在性谓词（GP-2/E3）：未映射 ⇒ 内核报 <c>probe_unmapped</c>，
    /// 不得与「端点缺失（endpoint_missing）」混同。实现必须显式声明（禁接口缺省 true 静默放行）。
    /// </summary>
    bool IsMapped(string entityType);
}

/// <summary>
/// 未装配探针（显式命名，禁默认值伪装事实）：恒返回 <c>false</c>。
/// <para>语义：无法证明存在 ⇒ 视为不存在（写前门 fail-closed）；内核另记 <c>probe-unassembled</c> 诊断，
/// 禁以恒真探针静默放行（原 <c>PermissiveEntityExistenceProbe</c> 已删除）。</para>
/// </summary>
public sealed class UnassembledEntityExistenceProbe : IEntityExistenceProbe
{
    public static readonly UnassembledEntityExistenceProbe Instance = new();

    public Task<bool> ExistsAsync(string entityType, string id, CancellationToken ct = default)
        => Task.FromResult(false);

    /// <summary>未装配 ⇒ 没有任何类型被映射（内核据此报 <c>probe_unmapped</c>）。</summary>
    public bool IsMapped(string entityType) => false;
}

/// <summary>关系诊断出口（结构化；未装配时由内核把原因写进 <see cref="RelationResult.Diagnostics"/>）。</summary>
public interface IRelationDiagnostics
{
    void RelationRejected(string operation, string? edge, string reason);

    void RelationDuplicate(string operation, string? edge, string reason);

    void RelationMissingEndpoint(string operation, string? edge, string endpoint);

    void RelationInvariantViolation(string operation, string invariant, string reason);

    void RelationPlanMissing(string reason);
}

/// <summary>诊断收集器（测试/观测装配用；线程内顺序即事件序）。</summary>
public sealed class RelationDiagnosticsCollector : IRelationDiagnostics
{
    private readonly List<string> _entries = new();

    public IReadOnlyList<string> Entries => _entries;

    public void RelationRejected(string operation, string? edge, string reason)
        => _entries.Add($"rejected|{operation}|{edge}|{reason}");

    public void RelationDuplicate(string operation, string? edge, string reason)
        => _entries.Add($"duplicate|{operation}|{edge}|{reason}");

    public void RelationMissingEndpoint(string operation, string? edge, string endpoint)
        => _entries.Add($"missing-endpoint|{operation}|{edge}|{endpoint}");

    public void RelationInvariantViolation(string operation, string invariant, string reason)
        => _entries.Add($"invariant|{operation}|{invariant}|{reason}");

    public void RelationPlanMissing(string reason)
        => _entries.Add($"plan-missing|{reason}");
}
