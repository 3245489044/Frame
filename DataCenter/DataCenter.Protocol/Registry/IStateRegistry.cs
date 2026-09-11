namespace DataCenter.Protocol.Registry;

/// <summary>
/// 类型注册表（DF7：注册制而非封闭枚举）—— 框架唯一"类型知识"的只读消费面。
/// 默认集（config/session/transient、state.value/... 、structure.owns/...）仅为示例：
/// 扩展 = 注册一行（域侧装配期注册），不改框架代码、不改文法。
/// 权威: Frame\DataCenter\Doc\DataCenter-可追踪数据流图框架-设计与实施.md §4.6 / §6.1。
/// </summary>
public interface IStateRegistry
{
    /// <summary>已登记的分层标识（tier）集；默认示例 config/session/transient。</summary>
    IReadOnlyCollection<string> Tiers { get; }

    /// <summary>已登记的节点种类（nodeKind）集；如 state.value / state.scope / state.trace（域内种类由域扩展登记）。</summary>
    IReadOnlyCollection<string> NodeKinds { get; }

    /// <summary>已登记的边种类（edgeKind）集；如 structure.owns / causality.derives。</summary>
    IReadOnlyCollection<string> EdgeKinds { get; }

    /// <summary>
    /// 已登记的域别名表（命名空间 → 逻辑域）—— 供"一个逻辑域的多个命名空间"在投影时归并为同一 domain
    /// （否则跨命名空间因果链无法闭合）。机制通用：域侧装配期登记，Core 不含任何具体域名。
    /// </summary>
    IReadOnlyDictionary<string, string> DomainAliases { get; }

    /// <summary>命名空间 → 逻辑域 的别名解析（未登记返回 false）。</summary>
    bool TryResolveDomainAlias(string ns, out string domain);

    /// <summary>tier 是否已登记（未登记 = 投影/门禁应报缺口，而非静默放行）。</summary>
    bool IsTierRegistered(string tier);

    /// <summary>nodeKind 是否已登记（K5 判据：未注册 Kind 数 = 0）。</summary>
    bool IsNodeKindRegistered(string nodeKind);

    /// <summary>edgeKind 是否已登记（K5 判据：未注册 Kind 数 = 0）。</summary>
    bool IsEdgeKindRegistered(string edgeKind);
}
