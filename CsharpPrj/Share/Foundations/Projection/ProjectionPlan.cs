#nullable enable

using System;

namespace Share.Projection;

/// <summary>计划步骤声明（声明序 = 执行序）。</summary>
/// <param name="Id">步骤 id（compose/fold/visibility…）。</param>
/// <param name="Input">输入类型。</param>
/// <param name="Output">输出类型。</param>
public sealed record ProjectionStepSpec(string Id, Type Input, Type Output);

/// <summary>投影计划（有序命名步骤）；声明缺步骤 ⇒ Violation，禁静默跳过。</summary>
/// <param name="Family">家族。</param>
/// <param name="Steps">步骤声明序。</param>
/// <param name="CarrierId">目标载体（可空）。</param>
public sealed record ProjectionPlan(
    string Family,
    System.Collections.Generic.IReadOnlyList<ProjectionStepSpec> Steps,
    string? CarrierId = null)
{
    /// <summary>空计划（无步骤 ⇒ Host 判 Violation）。</summary>
    public static ProjectionPlan Empty(string family) => new(family, Array.Empty<ProjectionStepSpec>());
}
