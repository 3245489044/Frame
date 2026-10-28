namespace UniversalWorkflowFramework.Relations;

/// <summary>
/// 容器解析状态（三态；治「解析不出」被当成「不一致」而误拒）。
/// <para><see cref="Resolved"/> = 已取到容器值；<see cref="NotApplicable"/> = 该端点类型不参与容器归属；
/// <see cref="Unresolvable"/> = 无法判定（键不同形 / 多族歧义 / 实体行不存在 / 键空间未装配）——
/// 必须可见化，**不得**据以判 <c>container-mismatch</c>（不可判定 ≠ 违规）。</para>
/// </summary>
public enum RelationContainerStatus
{
    /// <summary>已解析出容器键。</summary>
    Resolved = 0,

    /// <summary>该实体类型不需要容器归属（非 node 端点等）。</summary>
    NotApplicable = 1,

    /// <summary>无法判定（可见化；不构成拒绝依据）。</summary>
    Unresolvable = 2,
}

/// <summary>容器解析结果（唯一判据载体：状态 + 值 + 原因码）。</summary>
/// <param name="Status">三态。</param>
/// <param name="Container">容器键（仅 <see cref="RelationContainerStatus.Resolved"/> 非空）。</param>
/// <param name="Reason">原因码（<c>ok</c> / <c>entity-not-container-scoped:*</c> / <c>container-unresolvable</c> / …）。</param>
public sealed record RelationContainerResolution(
    RelationContainerStatus Status, string? Container, string Reason)
{
    /// <summary>解析成功。</summary>
    public static RelationContainerResolution Resolved(string container)
        => new(RelationContainerStatus.Resolved, container, "ok");

    /// <summary>该端点不参与容器归属。</summary>
    public static RelationContainerResolution NotApplicable(string reason)
        => new(RelationContainerStatus.NotApplicable, null, reason);

    /// <summary>无法判定（可见化）。</summary>
    public static RelationContainerResolution Unresolvable(string reason)
        => new(RelationContainerStatus.Unresolvable, null, reason);
}

/// <summary>
/// 容器归属解析端口（实体行容器键的唯一读取点）。
/// <para>声明面 <c>[Relation] container=</c> 的写前判据：容器与期望不一致 ⇒
/// <c>container-mismatch</c>（<c>on_mismatch=reject</c> 拒绝 / <c>report</c> 可见化后继续）。</para>
/// </summary>
public interface IRelationContainerResolver
{
    /// <summary>解析实体行的容器键（三态；禁伪造空串，禁把「无法判定」当「不一致」）。</summary>
    Task<RelationContainerResolution> ResolveAsync(string entityType, string key, CancellationToken ct = default);
}

