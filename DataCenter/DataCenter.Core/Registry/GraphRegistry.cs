using DataCenter.Protocol.Registry;

namespace Operation.StateBlock.Registry;

/// <summary>
/// 类型注册表（DF7）—— 全框架唯一"类型知识"来源的只读实现；默认集只是<b>示例</b>，扩展 = 注册一行。
/// 注册表由装配期构造（域侧可 <c>WithNodeKinds/WithEdgeKinds/WithTiers</c> 增条目），运行期只读。
/// 权威: 设计文档 §4.3（边类型归一表）/§4.6（注册项清单）。
/// </summary>
public sealed class GraphRegistry : IStateRegistry
{
    private readonly HashSet<string> _tiers;
    private readonly HashSet<string> _nodeKinds;
    private readonly HashSet<string> _edgeKinds;

    public GraphRegistry(
        IEnumerable<string>? tiers = null,
        IEnumerable<string>? nodeKinds = null,
        IEnumerable<string>? edgeKinds = null)
    {
        _tiers = NewSet(tiers, DefaultTiers);
        _nodeKinds = NewSet(nodeKinds, DefaultNodeKinds);
        _edgeKinds = NewSet(edgeKinds, DefaultEdgeKinds);
    }

    /// <summary>默认注册集（示例；域扩展经增量复制，不改默认集）。</summary>
    public static GraphRegistry CreateDefault() => new();

    /// <summary>默认分层标识（示例集，非封闭枚举；schema metadata.tiers 为线上注册表）。</summary>
    public static IReadOnlyCollection<string> DefaultTiers { get; } = ["config", "session", "transient"];

    /// <summary>默认节点种类（通用示例集）：状态值/分区锚点/追踪/通用产出。</summary>
    /// <remarks>
    /// 域内种类（如域特有的记录/任务语义）由域扩展在装配期经 <see cref="WithNodeKinds"/> 登记（DF7：注册一行），
    /// Core 保持通用、不出现域词汇（G1）。
    /// </remarks>
    public static IReadOnlyCollection<string> DefaultNodeKinds { get; } =
    [
        "state.value", "state.scope", "state.trace", "task", "node.output", "resource",
    ];

    /// <summary>默认边种类（示例集）：结构/因果/数据/引用/控制（分类决定 DF5 删除策略）。</summary>
    public static IReadOnlyCollection<string> DefaultEdgeKinds { get; } =
    [
        "structure.owns", "structure.session_owns", "structure.sequence",
        "causality.derives", "causality.invokes",
        "data.cross_store", "data.keymap",
        "reference.exports", "reference.points_to",
        "control.transition",
    ];

    public IReadOnlyCollection<string> Tiers => _tiers;

    public IReadOnlyCollection<string> NodeKinds => _nodeKinds;

    public IReadOnlyCollection<string> EdgeKinds => _edgeKinds;

    public bool IsTierRegistered(string tier) => !string.IsNullOrWhiteSpace(tier) && _tiers.Contains(tier);

    public bool IsNodeKindRegistered(string nodeKind) => !string.IsNullOrWhiteSpace(nodeKind) && _nodeKinds.Contains(nodeKind);

    public bool IsEdgeKindRegistered(string edgeKind) => !string.IsNullOrWhiteSpace(edgeKind) && _edgeKinds.Contains(edgeKind);

    /// <summary>增量复制并追加分层标识（注册一行；原注册表不变）。</summary>
    public GraphRegistry WithTiers(params string[] tiers) => new(_tiers.Concat(tiers), _nodeKinds, _edgeKinds);

    /// <summary>增量复制并追加节点种类（注册一行；原注册表不变）。</summary>
    public GraphRegistry WithNodeKinds(params string[] nodeKinds) => new(_tiers, _nodeKinds.Concat(nodeKinds), _edgeKinds);

    /// <summary>增量复制并追加边种类（注册一行；原注册表不变）。</summary>
    public GraphRegistry WithEdgeKinds(params string[] edgeKinds) => new(_tiers, _nodeKinds, _edgeKinds.Concat(edgeKinds));

    private static HashSet<string> NewSet(IEnumerable<string>? declared, IReadOnlyCollection<string> fallback)
        => new(declared is null ? fallback : declared.Where(v => !string.IsNullOrWhiteSpace(v)), StringComparer.Ordinal);
}
