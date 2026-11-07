#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Share.Projection;

/// <summary>
/// 投影宿主（唯一入口）：校验 Plan → 执行 Step → 选 Carrier → 返回 Result。
/// <para>不含领域分支；不给具体声明类型开洞；缺步骤/载体不一致 ⇒ fail-closed。</para>
/// </summary>
public sealed class ProjectionHost
{
    /// <summary>执行一次投影（compose 为链式步骤的入口；当前契约只接受单一入口步骤）。</summary>
    /// <typeparam name="TSource">源。</typeparam>
    /// <typeparam name="TPlan">计划。</typeparam>
    /// <typeparam name="TArtifact">产物。</typeparam>
    public ProjectionResult<TArtifact> Project<TSource, TPlan, TArtifact>(
        TSource source,
        ProjectionPlan plan,
        ProjectionContext context,
        IProjectionStep<TSource, TPlan> compose,
        IProjectionCarrier<TPlan, TArtifact> carrier)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(compose);
        ArgumentNullException.ThrowIfNull(carrier);

        if (plan.Steps.Count == 0)
        {
            return ProjectionResult<TArtifact>.Violation(ProjectionReasons.StepMissing, "plan");
        }

        var carrierId = context.CarrierId ?? plan.CarrierId ?? carrier.CarrierId;
        if (!string.Equals(carrierId, carrier.CarrierId, StringComparison.Ordinal))
        {
            return ProjectionResult<TArtifact>.Violation(ProjectionReasons.CarrierMismatch, carrierId);
        }

        var composed = compose.Project(source, context);
        if (composed.Status == ProjectionStatus.Violation)
        {
            return new ProjectionResult<TArtifact>(
                ProjectionStatus.Violation,
                default!,
                composed.Diagnostics,
                composed.Unsupported,
                composed.Fingerprint);
        }

        var projected = carrier.Project(composed.Artifact, context);
        var diagnostics = composed.Diagnostics.Concat(projected.Diagnostics).ToList();
        return projected with
        {
            Diagnostics = diagnostics,
            Fingerprint = projected.Fingerprint ?? composed.Fingerprint,
        };
    }
}
