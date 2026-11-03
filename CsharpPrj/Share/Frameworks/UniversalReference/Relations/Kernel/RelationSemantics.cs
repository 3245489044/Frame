using System;
using System.Collections.Generic;
using System.Linq;

namespace UniversalWorkflowFramework.Relations.Kernel;

/// <summary>请求派生视图（端口 DTO 保持无方法体；计算集中于此）。</summary>
public static class RelationRequests
{
    /// <summary>目标键集（单键与批量归一；缺省 ⇒ 空集）。</summary>
    public static IReadOnlyList<string> Targets(this RelationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        return request.TargetKeys is { Count: > 0 } many
            ? many
            : request.TargetKey is { Length: > 0 } one ? new[] { one } : Array.Empty<string>();
    }
}

/// <summary>写结果工厂（唯一构造点；禁调用点各自拼装失败形态）。</summary>
public static class RelationResults
{
    public static RelationResult Rejected(string operation, string? edge, string reason)
        => new(false, operation, edge, 0, new[] { reason }, RelationOutcome.Rejected);
}

/// <summary>读结果工厂（空集必带空因）。</summary>
public static class RelationQueryResults
{
    public static RelationQueryResult Empty(string reason) => new(Array.Empty<string>(), new[] { reason });
}

/// <summary>容器解析结果工厂（三态唯一构造点）。</summary>
public static class RelationContainerResults
{
    public static RelationContainerResolution Resolved(string container)
        => new(RelationContainerStatus.Resolved, container, "ok");

    public static RelationContainerResolution NotApplicable(string reason)
        => new(RelationContainerStatus.NotApplicable, null, reason);

    public static RelationContainerResolution Unresolvable(string reason)
        => new(RelationContainerStatus.Unresolvable, null, reason);
}

/// <summary>声明操作的结构事实派生（禁调用点各自解析 intent/container）。</summary>
public static class RelationOperations
{
    /// <summary>删除意图（唯一来源 = 声明面 <c>intent=</c>；禁由 operation 名推断）。</summary>
    public static bool IsRemove(this RelationOperation operation)
        => string.Equals(operation.Intent, "remove", StringComparison.Ordinal);

    /// <summary>是否存在 <c>exists=</c> 声明；缺省 = <c>src,tgt</c>。</summary>
    public static bool Probes(this RelationOperation operation, string side)
        => operation.Exists.Count == 0 || operation.Exists.Contains(side, StringComparer.Ordinal);

    /// <summary>容器归属校验是否启用（<c>container=</c> 非空）。</summary>
    public static bool ChecksContainer(this RelationOperation operation)
        => !string.IsNullOrWhiteSpace(operation.Container);

    /// <summary>容器不一致处置（<c>on_mismatch=reject|report</c>；缺省 reject）。</summary>
    public static bool ContainerRejects(this RelationOperation operation)
        => !string.Equals(operation.OnMismatch, "report", StringComparison.Ordinal);
}

/// <summary>不变量 → 守卫的编译点（未声明 pre/post ⇒ null；禁伪造空守卫）。</summary>
public static class RelationInvariants
{
    /// <summary>是否声明了守卫谓词（pre/post 任一非空）。</summary>
    public static bool HasGuard(this RelationInvariant invariant)
        => (invariant.Pre?.Count ?? 0) > 0 || (invariant.Post?.Count ?? 0) > 0;

    /// <summary>编译为守卫（未声明 pre/post ⇒ null）。</summary>
    public static RelationGuard? ToGuard(this RelationInvariant invariant)
        => invariant.HasGuard()
            ? new RelationGuard(
                invariant.Id, invariant.Edge,
                invariant.Pre ?? Array.Empty<string>(), invariant.Post ?? Array.Empty<string>(),
                invariant.Posture, invariant.OnViolation)
            : null;
}

/// <summary>空计划占位（声明面缺失时的显式值；禁以空目录冒充已装配）。</summary>
public static class RelationPlans
{
    public static readonly RelationPlan Empty = new(
        "0", Array.Empty<RelationOperation>(), Array.Empty<RelationInvariant>(), Array.Empty<RelationDerivation>());
}
