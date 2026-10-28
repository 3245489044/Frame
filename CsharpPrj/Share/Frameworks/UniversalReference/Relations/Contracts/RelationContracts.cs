using UniversalWorkflowFramework.Core.Models;
using UniversalWorkflowFramework.StorageFramework.Abstractions;

namespace UniversalWorkflowFramework.Relations;

// 命名空间已收敛为 UniversalWorkflowFramework.Relations（R5）；Operation 侧消费方经同一命名空间引用，源码零改动。

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
    string? Operation = null)
{
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
    public int Revived { get; init; }

    public IReadOnlyList<string> ReviveReasons { get; init; } = Array.Empty<string>();

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
