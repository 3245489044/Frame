using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace Share.Composition.AssemblyFramework;

/// <summary>
/// 存储域端口注册表（通用件）—— 把「域键 → 端口」的多张平行字典收拢为一处注册与查询，
/// 端口可为惰性工厂（未装配 ⇒ 查询 false），也可为现成实例。
/// <para>零领域类型：域键与端口语义由调用方定义；查询按精确键（trim），不做子串猜域。</para>
/// </summary>
public sealed class StoreDomainRegistry<TPort> where TPort : class
{
    private readonly object _gate = new();
    private readonly Dictionary<string, Func<TPort?>> _providers = new(StringComparer.Ordinal);

    /// <summary>登记现成端口实例。</summary>
    public StoreDomainRegistry<TPort> Register(string domain, TPort value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentNullException.ThrowIfNull(value);
        return RegisterFactory(domain, () => value);
    }

    /// <summary>登记惰性端口工厂（每次查询实时解析；返回 null = 未装配）。</summary>
    public StoreDomainRegistry<TPort> RegisterFactory(string domain, Func<TPort?> factory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(domain);
        ArgumentNullException.ThrowIfNull(factory);
        lock (_gate) _providers[domain.Trim()] = factory;
        return this;
    }

    /// <summary>取某域的端口（未登记 / 未装配 ⇒ false，调用方负责可见化拒绝）。</summary>
    public bool TryGet(string? domain, [MaybeNullWhen(false)] out TPort port)
    {
        port = null;
        if (string.IsNullOrWhiteSpace(domain)) return false;
        Func<TPort?>? factory;
        lock (_gate)
        {
            if (!_providers.TryGetValue(domain!.Trim(), out factory)) return false;
        }

        port = factory();
        return port is not null;
    }

    /// <summary>是否登记了该域（不触发工厂解析）。</summary>
    public bool Contains(string? domain)
    {
        if (string.IsNullOrWhiteSpace(domain)) return false;
        lock (_gate) return _providers.ContainsKey(domain!.Trim());
    }

    /// <summary>已登记域键快照（稳定序）。</summary>
    public IReadOnlyList<string> Domains()
    {
        lock (_gate) return _providers.Keys.OrderBy(static d => d, StringComparer.Ordinal).ToArray();
    }

    /// <summary>「域 → 惰性工厂」快照（供需要字典形态的调用方复用，避免复制第二套注册表）。</summary>
    public IReadOnlyDictionary<string, Func<TPort?>> ProviderMap()
    {
        lock (_gate) return new Dictionary<string, Func<TPort?>>(_providers, StringComparer.Ordinal);
    }
}
