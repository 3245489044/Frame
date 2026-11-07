#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.Projection;

/// <summary>投影结果五态（与 ReadOutcome 互转）。</summary>
public enum ProjectionStatus
{
    /// <summary>成功。</summary>
    Ok = 0,

    /// <summary>源为空（合法空态）。</summary>
    Empty = 1,

    /// <summary>源未装配。</summary>
    NotAttached = 2,

    /// <summary>版本陈旧。</summary>
    Stale = 3,

    /// <summary>契约违规（声明/步骤/载体缺失）。</summary>
    Violation = 4,
}

/// <summary>诊断项（可见化原因码）。</summary>
/// <param name="Code">原因码。</param>
/// <param name="Detail">细节（可空）。</param>
public sealed record ProjectionDiagnostic(string Code, string? Detail = null);

/// <summary>载体不支持的槽位（可见降级）。</summary>
/// <param name="CarrierId">载体。</param>
/// <param name="Slot">槽位/角色/单元。</param>
/// <param name="Reason">原因码。</param>
public sealed record UnsupportedProjection(string CarrierId, string Slot, string Reason);

/// <summary>投影产物 + 状态 + 诊断 + 不支持项 + 指纹。</summary>
/// <typeparam name="T">产物类型。</typeparam>
public sealed record ProjectionResult<T>(
    ProjectionStatus Status,
    T Artifact,
    IReadOnlyList<ProjectionDiagnostic> Diagnostics,
    IReadOnlyList<UnsupportedProjection> Unsupported,
    string? Fingerprint = null)
{
    /// <summary>成功。</summary>
    public static ProjectionResult<T> Ok(T artifact, string? fingerprint = null)
        => new(ProjectionStatus.Ok, artifact, Array.Empty<ProjectionDiagnostic>(), Array.Empty<UnsupportedProjection>(), fingerprint);

    /// <summary>空态。</summary>
    public static ProjectionResult<T> Empty(T artifact)
        => new(ProjectionStatus.Empty, artifact, Array.Empty<ProjectionDiagnostic>(), Array.Empty<UnsupportedProjection>());

    /// <summary>未装配。</summary>
    public static ProjectionResult<T> NotAttached(string code)
        => new(ProjectionStatus.NotAttached, default!, new[] { new ProjectionDiagnostic(code) }, Array.Empty<UnsupportedProjection>());

    /// <summary>违规（fail-closed；Host 不调 Carrier）。</summary>
    public static ProjectionResult<T> Violation(string code, string? detail = null)
        => new(ProjectionStatus.Violation, default!, new[] { new ProjectionDiagnostic(code, detail) }, Array.Empty<UnsupportedProjection>());

    /// <summary>合并诊断（本结果状态优先）。</summary>
    public ProjectionResult<T> WithDiagnostics(IEnumerable<ProjectionDiagnostic> diagnostics)
        => this with { Diagnostics = Diagnostics.Concat(diagnostics).ToList() };

    /// <summary>合并不支持项。</summary>
    public ProjectionResult<T> WithUnsupported(IEnumerable<UnsupportedProjection> unsupported)
        => this with { Unsupported = Unsupported.Concat(unsupported).ToList() };
}
