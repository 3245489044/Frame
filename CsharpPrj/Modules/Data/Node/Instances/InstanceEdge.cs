namespace Modules.Node.Instances;

/// <summary>
/// 跨实例边类别（G-5）—— 只读关系，不承载可变共享状态（I5）；顺序不依赖边（N6）。
/// <para>级联映射单源：<c>Owns ↔ Holds</c>（父处置即级联），<c>Link</c>/<c>Ref</c> 不级联（多对多保留为边）。</para>
/// </summary>
public enum InstanceEdgeKind
{
    /// <summary>持有（包含关系；父处置级联子实例）。</summary>
    Owns,

    /// <summary>连接（多对多业务关联；不级联）。</summary>
    Link,

    /// <summary>引用（只读引用；不级联）。</summary>
    Ref,
}

/// <summary>一条跨实例边（有 owner 的只读关系；幂等键 = <see cref="EdgeKey"/>）。</summary>
/// <param name="From">源实例（边的 owner 侧）。</param>
/// <param name="To">目标实例。</param>
/// <param name="Kind">边类别。</param>
public sealed record InstanceEdge(InstanceId From, InstanceId To, InstanceEdgeKind Kind)
{
    /// <summary>边键（**唯一拼法**：幂等/去重键，禁各处手拼）。</summary>
    public string EdgeKey => $"{From}->{To}#{Kind}";

    /// <summary>该边类别在父处置时是否级联（单源 = <see cref="InstanceLifecycle.CascadesOnDispose"/> 的边侧映射）。</summary>
    public bool CascadesOnDispose => Kind == InstanceEdgeKind.Owns;
}

/// <summary>
/// 跨实例边端口（G-5）—— 跨实例只经边（I5）：只增不丢、幂等、顺序不依赖边（复用 N4/N5/N6）。
/// <para>实现中立：图库 / 表 / 内存皆可；框架内只提供内存基准实现。</para>
/// </summary>
public interface IInstanceEdgeStore
{
    /// <summary>加边（幂等：已存在 ⇒ false，不抛、不重复计）。</summary>
    Task<bool> AddAsync(InstanceEdge edge, CancellationToken cancellationToken = default);

    /// <summary>撤边（不存在 ⇒ false）。</summary>
    Task<bool> RemoveAsync(InstanceEdge edge, CancellationToken cancellationToken = default);

    /// <summary>取某实例的关联边（可按类别过滤；序 = 边键序数序，稳定可比）。</summary>
    Task<IReadOnlyList<InstanceEdge>> EdgesOfAsync(InstanceId id, InstanceEdgeKind? kind = null, CancellationToken cancellationToken = default);

    /// <summary>取全部边（可按类别过滤；诊断/对账用）。</summary>
    Task<IReadOnlyList<InstanceEdge>> AllAsync(InstanceEdgeKind? kind = null, CancellationToken cancellationToken = default);
}

/// <summary>内存边表（G-5 基准实现）：幂等加边 / 撤边 / 前缀无关的稳定枚举（契约测试基准靶）。</summary>
public sealed class InMemoryInstanceEdgeStore : IInstanceEdgeStore
{
    private readonly Dictionary<string, InstanceEdge> _edges = new(StringComparer.Ordinal);
    private readonly object _sync = new();

    /// <summary>当前边数（诊断用）。</summary>
    public int Count
    {
        get
        {
            lock (_sync)
            {
                return _edges.Count;
            }
        }
    }

    public Task<bool> AddAsync(InstanceEdge edge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edge);

        lock (_sync)
        {
            // 只增不丢 + 幂等：同边重复添加不改状态（N4），且不因重复而抛（边是关系非事实记录）。
            return Task.FromResult(_edges.TryAdd(edge.EdgeKey, edge));
        }
    }

    public Task<bool> RemoveAsync(InstanceEdge edge, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(edge);

        lock (_sync)
        {
            return Task.FromResult(_edges.Remove(edge.EdgeKey));
        }
    }

    public Task<IReadOnlyList<InstanceEdge>> EdgesOfAsync(
        InstanceId id, InstanceEdgeKind? kind = null, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            IReadOnlyList<InstanceEdge> edges = _edges.Values
                .Where(edge => edge.From == id || edge.To == id)
                .Where(edge => kind is null || edge.Kind == kind)
                .OrderBy(static edge => edge.EdgeKey, StringComparer.Ordinal)
                .ToArray();
            return Task.FromResult(edges);
        }
    }

    public Task<IReadOnlyList<InstanceEdge>> AllAsync(
        InstanceEdgeKind? kind = null, CancellationToken cancellationToken = default)
    {
        lock (_sync)
        {
            IReadOnlyList<InstanceEdge> edges = _edges.Values
                .Where(edge => kind is null || edge.Kind == kind)
                .OrderBy(static edge => edge.EdgeKey, StringComparer.Ordinal)
                .ToArray();
            return Task.FromResult(edges);
        }
    }
}
