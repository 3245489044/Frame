#nullable enable

namespace Share.Projection;

/// <summary>载体方言（Plan → Artifact）；缺映射 ⇒ 可见 Unsupported，禁静默。</summary>
/// <typeparam name="TPlan">计划。</typeparam>
/// <typeparam name="TArtifact">产物。</typeparam>
public interface IProjectionCarrier<TPlan, TArtifact>
{
    /// <summary>载体 id（声明面键）。</summary>
    string CarrierId { get; }

    /// <summary>纯投影。</summary>
    ProjectionResult<TArtifact> Project(TPlan plan, ProjectionContext context);
}
