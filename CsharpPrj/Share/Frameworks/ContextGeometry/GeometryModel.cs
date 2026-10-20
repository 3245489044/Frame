namespace Share.ContextGeometry;

/// <summary>
/// 前缀段（缓存几何最小可审计单位）：键 + 缓存带 + 内容摘要 + 可选内容寻址键。
/// <para><see cref="ContentRef"/> = 共享块内容键（同一内容多次引用只发射一次）。</para>
/// </summary>
public sealed record PrefixSegment(string Key, CacheBand Band, string Hash, string? ContentRef = null);

/// <summary>
/// 冻结前缀快照（内核视图）：段集合 + 聚合摘要 + 规范字节引用。
/// <para>语义：<see cref="CanonicalRef"/> 指向规范字节（由存储实现解析）；同摘要 ⇒ 同一串字节。</para>
/// </summary>
public sealed record GeometrySnapshot(
    string SnapshotId,
    string Scope,
    string AggregateDigest,
    IReadOnlyList<PrefixSegment> Segments,
    string? CanonicalRef = null,
    int Version = 1);

/// <summary>组合结果：摘要 + 段集合 + 规范字节引用 + 诊断（空 = 无异常）。</summary>
public sealed record ComposedPrefix(
    string Digest,
    IReadOnlyList<PrefixSegment> Segments,
    string? CanonicalRef,
    IReadOnlyList<string> Diagnostics);

/// <summary>前缀来源端口（领域侧实现：角色/声明 → 段；内核只收段）。</summary>
public interface IPrefixSource
{
    /// <summary>按作用域取段集合（声明序即语义）。</summary>
    IReadOnlyList<PrefixSegment> SegmentsFor(string scope);
}

/// <summary>内容寻址存储端口（宿主实现；内核只用键，不触 IO）。</summary>
public interface ISharedBlockStore
{
    /// <summary>按键解析引用（未命中 ⇒ false，不抛）。</summary>
    bool TryResolve(string contentKey, out string reference);

    /// <summary>登记内容（幂等：同键同引用，后写覆盖视为同一内容）。</summary>
    string Put(string contentKey, string canonicalBytes);
}
