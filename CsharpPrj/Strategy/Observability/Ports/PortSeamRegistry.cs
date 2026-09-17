using System.Collections.Concurrent;

namespace Strategy.Observability.Ports;

/// <summary>
/// 端口**实现注入提供方**（F2-d 一期；框架定义、宿主/测试侧实现）：<c>impl.seam</c> 声明的实现在
/// 执行面经本接口注册，内核只按**不透明名**解析，不关心实现细节。
/// <para>
/// 分工：契约在标准层（<c>PortSeamStandard</c>：有名字 / 有实参 / 有理由，可校验可登记）；实现在本层；
/// 测试侧只实现本接口 + 提交 seam 表，**不再自建派生工厂表**。
/// </para>
/// </summary>
public interface IPortSeamProvider
{
    /// <summary>seam 名（与台账 <c>impl.seam.seam</c> 逐字对应；不透明名，机制层不解释）。</summary>
    string Seam { get; }

    /// <summary>按声明实参解析实现（缺参即抛，防静默降级）。</summary>
    object? Resolve(IReadOnlyList<object?> args);
}

/// <summary>
/// seam 注册表（执行面）：seam 名 → provider 的**单点登记**；未注册即 fail-fast（不静默回落）。
/// <para>额度纪律：seam 条目数**只减不增**（守卫棘轮）；注册重复名即抛（单一实现点）。</para>
/// </summary>
public static class PortSeamRegistry
{
    private static readonly ConcurrentDictionary<string, IPortSeamProvider> Providers = new(StringComparer.Ordinal);

    /// <summary>已注册 seam 名（Ordinal 升序）。</summary>
    public static IReadOnlyList<string> Seams => Providers.Keys.OrderBy(name => name, StringComparer.Ordinal).ToArray();

    /// <summary>登记 provider（重名即抛：单一实现点被破坏）。</summary>
    public static void Register(IPortSeamProvider provider)
    {
        ArgumentNullException.ThrowIfNull(provider);
        if (string.IsNullOrWhiteSpace(provider.Seam))
            throw new ArgumentException("[port:seam] provider 未声明 seam 名", nameof(provider));

        if (!Providers.TryAdd(provider.Seam, provider))
            throw new InvalidOperationException(
                $"[port:seam] seam '{provider.Seam}' 重复登记（单一实现点被破坏）");
    }

    /// <summary>按名解析（未注册即抛，附已注册清单：防拼写漂移静默）。</summary>
    public static object? Resolve(string seam, IReadOnlyList<object?> args)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(seam);

        return Providers.TryGetValue(seam, out var provider)
            ? provider.Resolve(args)
            : throw new InvalidOperationException(
                $"[port:seam] seam '{seam}' 未注册（须由宿主/测试侧实现 IPortSeamProvider 并登记）。已注册：{string.Join(", ", Seams)}");
    }

    /// <summary>是否已注册（供装配期校验：声明了 seam 的端口须有 provider）。</summary>
    public static bool IsRegistered(string seam) => Providers.ContainsKey(seam);
}
