using ContractFramework.Abstractions;
using ContractFramework.Catalog;

namespace ContractFramework.Registry;

/// <summary>
/// 契约注册表：注册 / 查询 / 反向引用 / 域分组。
/// <para>执行体注册唯一（每 <see cref="ContractHostKind"/> 恰一个适配器；适配器按 <c>host.impl</c> 内部派发）。</para>
/// <para>注意：本类型与 <c>Operation/FrontEndDriven/Shared/ContractRegistry.cs</c>（JSON 控件 Schema 契约）**同名不同物**，
/// 后者属既有资产；本框架一律以 <c>ContractFramework.*</c> 命名空间限定，不并入。</para>
/// </summary>
public sealed class ContractRegistry
{
    private readonly Dictionary<string, ContractDescriptor> _byId = new(StringComparer.Ordinal);
    private readonly Dictionary<ContractHostKind, IContractHost> _hosts = [];
    private readonly List<string> _conflicts = [];

    /// <summary>已注册契约（Id 唯一）。</summary>
    public IReadOnlyCollection<ContractDescriptor> Contracts => _byId.Values;

    /// <summary>已注册执行体（每种类至多一个）。</summary>
    public IReadOnlyCollection<IContractHost> Hosts => _hosts.Values;

    /// <summary>注册冲突（重复 Id / 重复执行体种类）——可见化，不静默覆盖。</summary>
    public IReadOnlyList<string> Conflicts => _conflicts;

    /// <summary>注册契约（重复 Id 保留首见并记冲突）。</summary>
    public void Register(ContractDescriptor contract)
    {
        ArgumentNullException.ThrowIfNull(contract);
        var key = contract.Id.Raw;
        if (key.Length == 0)
        {
            _conflicts.Add("注册拒绝：契约缺 id（须先补齐 Id 再注册）");
            return;
        }

        if (_byId.ContainsKey(key))
        {
            _conflicts.Add($"重复注册：{key}（保留首见）");
            return;
        }

        _byId[key] = contract;
    }

    /// <summary>注册执行体适配器（同种类重复 ⇒ 冲突，保留首见）。</summary>
    public void RegisterHost(IContractHost host)
    {
        ArgumentNullException.ThrowIfNull(host);
        if (_hosts.ContainsKey(host.Kind))
        {
            _conflicts.Add($"重复执行体种类：{host.Kind}（保留首见）");
            return;
        }

        _hosts[host.Kind] = host;
    }

    /// <summary>按原始 Id 串取契约。</summary>
    public ContractDescriptor? Get(string id)
        => id is not null && _byId.TryGetValue(id, out var contract) ? contract : null;

    /// <summary>按 Id 值对象取契约。</summary>
    public ContractDescriptor? Get(ContractId id) => Get(id?.Raw ?? string.Empty);

    /// <summary>解析执行体（缺引用 / 未识别种类 / 未注册 ⇒ <c>null</c> ⇒ 判 <c>⊥</c>）。</summary>
    public IContractHost? ResolveHost(ContractHostRef? hostRef)
    {
        if (hostRef is null || !hostRef.IsWellFormed)
        {
            return null;
        }

        return _hosts.TryGetValue(hostRef.Kind, out var host) ? host : null;
    }

    /// <summary>按不变量族分组。</summary>
    public IEnumerable<ContractDescriptor> ByDomain(string? family)
        => _byId.Values.Where(c => string.Equals(c.Id.Family, family, StringComparison.Ordinal));

    /// <summary>按层级分组。</summary>
    public IEnumerable<ContractDescriptor> ByTier(ContractTier tier)
        => _byId.Values.Where(c => c.Tier == tier);

    /// <summary>反向引用（谁引用了 <paramref name="symbol"/>：Id 或 host.impl 含该符号）。</summary>
    public IEnumerable<ContractDescriptor> ReverseReferences(string symbol)
    {
        if (string.IsNullOrWhiteSpace(symbol))
        {
            return [];
        }

        return _byId.Values.Where(c =>
            c.Id.Raw.Contains(symbol, StringComparison.Ordinal)
            || (c.Host?.Impl.Contains(symbol, StringComparison.Ordinal) ?? false));
    }

    /// <summary>由目录装配注册表。</summary>
    public static ContractRegistry FromCatalog(ContractCatalog catalog, IEnumerable<IContractHost>? hosts = null)
    {
        var registry = new ContractRegistry();
        if (catalog is not null)
        {
            foreach (var contract in catalog.Descriptors)
            {
                registry.Register(contract);
            }
        }

        if (hosts is not null)
        {
            foreach (var host in hosts)
            {
                registry.RegisterHost(host);
            }
        }

        return registry;
    }
}
