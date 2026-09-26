using System.Collections.Generic;

namespace Modules.Node.Placement;

/// <summary>载荷形态（N11：载荷恒为 JSON 文档，形态差异由位置声明）。</summary>
public enum PlacementForm
{
    /// <summary>外部原文（如上游落盘的 messages.json）。</summary>
    Raw,

    /// <summary>规范化视图。</summary>
    View,

    /// <summary>摘要（内容哈希）。</summary>
    Digest,
}

/// <summary>保留策略（读侧语义标签，不驱动写侧另存副本 N7）。</summary>
public enum PlacementRetention
{
    /// <summary>供模型消费。</summary>
    Contextual,

    /// <summary>仅供审计/回灌。</summary>
    Audit,

    /// <summary>仅供展示。</summary>
    Presentational,
}

/// <summary>位置状态（N10 灰度退役）。</summary>
public enum PlacementState
{
    Active,
    Legacy,
    Retired,
}

/// <summary>
/// 位置声明（N2/N8）：<c>Placement = ⟨PlacementId, Domain, LevelId, CarrierId, KeyTemplate, Form, Retention, SelectionId, WriterId, State⟩</c>。
/// 位置由 <c>loc</c> 反解或声明装载产生（Origin=Derived 可标注）；框架内禁硬编码 loc→carrier（P22）。
/// </summary>
public sealed record PlacementSpec(
    string PlacementId,
    string Domain,
    string LevelId,
    string CarrierId,
    string KeyTemplate,
    PlacementForm Form,
    PlacementRetention Retention,
    string SelectionId,
    string WriterId,
    PlacementState State = PlacementState.Active,
    IReadOnlyList<string>? LocalCoordinates = null)
{
    /// <summary>
    /// 本位置**自有坐标**名集（键模板中不属身份占位符的字段，如轮域 <c>task-node</c> 的 <c>storage</c>/<c>node</c>）。
    /// 判据（<c>Modules.Node.Spec.NodeSpecValidator</c>）：位置键字段须 ⊆ 身份占位符 ∪ 本集；未声明即 Error（禁隐式坐标）。
    /// </summary>
    public IReadOnlyList<string> LocalCoordinatesOrEmpty => LocalCoordinates ?? System.Array.Empty<string>();

    public bool IsWritable => State == PlacementState.Active;

    public bool IsReadable => State != PlacementState.Retired;
}

/// <summary>已定位的放置点：位置键 = 键模板(身份键)。</summary>
public sealed record NodePlacement(PlacementSpec Spec, string PlacementKey);

/// <summary>位置注册表（声明装载 + loc 反解产物的唯一容纳处）。</summary>
public sealed class PlacementRegistry
{
    private readonly Dictionary<string, PlacementSpec> _byId = new(StringComparer.Ordinal);

    public IReadOnlyCollection<PlacementSpec> All => _byId.Values.ToArray();

    public int Count => _byId.Count;

    public void Add(PlacementSpec spec)
    {
        if (!_byId.TryAdd(spec.PlacementId, spec))
        {
            throw new InvalidOperationException($"位置声明重复: {spec.PlacementId}");
        }
    }

    public void AddOrReplace(PlacementSpec spec) => _byId[spec.PlacementId] = spec;

    public bool TryGet(string placementId, out PlacementSpec spec)
    {
        if (_byId.TryGetValue(placementId, out var found))
        {
            spec = found;
            return true;
        }

        spec = null!;
        return false;
    }

    public IReadOnlyList<PlacementSpec> ByDomain(string domain) =>
        _byId.Values.Where(s => string.Equals(s.Domain, domain, StringComparison.Ordinal)).ToArray();

    public IReadOnlyList<PlacementSpec> ByCarrier(string carrierId) =>
        _byId.Values.Where(s => string.Equals(s.CarrierId, carrierId, StringComparison.Ordinal)).ToArray();

    /// <summary>写侧候选：仅 Active（Legacy 拒写、Retired 不再命中）。</summary>
    public IReadOnlyList<PlacementSpec> WritableByDomain(string domain) =>
        ByDomain(domain).Where(s => s.IsWritable).ToArray();

    /// <summary>读侧候选：非 Retired。</summary>
    public IReadOnlyList<PlacementSpec> ReadableByDomain(string domain) =>
        ByDomain(domain).Where(s => s.IsReadable).ToArray();
}
