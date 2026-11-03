namespace UniversalWorkflowFramework.Relations;

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

/// <summary>未装配策略（plan 编译失败时的显式占位）：一切边均视为未声明/未废弃。</summary>
public sealed class UnassembledRelationPolicy : IRelationPolicy
{
    public static readonly UnassembledRelationPolicy Instance = new();

    public RelationEdgeSpec? FindEdge(string edgeName) => null;

    public RelationConstraintSpec? FindConstraint(string edgeName) => null;

    public bool IsDeclared(string edgeName) => false;

    public bool IsDeprecated(string edgeName) => false;
}

/// <summary>工作流命名空间单源端口：所有写边 Location 的唯一取值点。</summary>
public interface IWorkflowNamespace
{
    string Default { get; }

    string Resolve(string? explicitValue, string fallbackKey);
}

/// <summary>端点存在性探针（写前判据；未装配 ⇒ <see cref="UnassembledEntityExistenceProbe"/>）。</summary>
public interface IEntityExistenceProbe
{
    Task<bool> ExistsAsync(string entityType, string id, CancellationToken ct = default);

    bool IsMapped(string entityType);
}

/// <summary>未装配探针（显式命名，禁默认值伪装事实）：恒返回 <c>false</c>。</summary>
public sealed class UnassembledEntityExistenceProbe : IEntityExistenceProbe
{
    public static readonly UnassembledEntityExistenceProbe Instance = new();

    public Task<bool> ExistsAsync(string entityType, string id, CancellationToken ct = default)
        => Task.FromResult(false);

    public bool IsMapped(string entityType) => false;
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

    /// <summary>判据不适用（合法跳过：写入本身不受影响，不计失败 —— 禁表述为「未落库」）。</summary>
    void RelationWriteNotApplicable(string operation, string? edge, string reason, string role, string key);

    /// <summary>判据不可用（未装配 / 无法解析：跳过校验但不据此拒绝，属能力缺失可见化）。</summary>
    void RelationWriteUnavailable(string operation, string? edge, string code, string role, string key);
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

    public void RelationProbeUnassembled(string operation, string? edge, string reason)
        => _entries.Add($"probe-unassembled|{operation}|{edge}|{reason}");

    public void RelationProbeUnmapped(string operation, string? edge, string endpoints)
        => _entries.Add($"probe-unmapped|{operation}|{edge}|{endpoints}");

    public void RelationInvariantViolation(string operation, string invariant, string reason)
        => _entries.Add($"invariant|{operation}|{invariant}|{reason}");

    public void RelationPlanMissing(string reason)
        => _entries.Add($"plan-missing|{reason}");

    public void RelationRevived(string edge, string sourceKey, string targetKey, string? tombstoneReason)
        => _entries.Add($"revived|{edge}|{sourceKey}->{targetKey}|{tombstoneReason ?? string.Empty}");

    public void RelationWriteSkipped(string operation, string? edge, string verdict, string role, string key)
        => _entries.Add($"write-skipped|{operation}|{edge}|{verdict}|{role}|{key}");

    public void RelationWriteNotApplicable(string operation, string? edge, string reason, string role, string key)
        => _entries.Add($"write-not-applicable|{operation}|{edge}|{reason}|{role}|{key}");

    public void RelationWriteUnavailable(string operation, string? edge, string code, string role, string key)
        => _entries.Add($"write-unavailable|{operation}|{edge}|{code}|{role}|{key}");
}
