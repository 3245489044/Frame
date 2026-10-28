namespace Operation.DataStoreDriven.Relations;

/// <summary>
/// 容器归属解析端口（实体行容器键的唯一读取点）。
/// <para>声明面 <c>[Relation] container=</c> 的写前判据：容器与期望不一致 ⇒
/// <c>container-mismatch</c>（<c>on_mismatch=reject</c> 拒绝 / <c>report</c> 可见化后继续）。</para>
/// </summary>
public interface IRelationContainerResolver
{
    /// <summary>取实体行的容器键（不存在 ⇒ null；禁伪造空串）。</summary>
    Task<string?> GetContainerAsync(string entityType, string key, CancellationToken ct = default);
}
