using System.Collections.ObjectModel;

namespace Share.Frameworks.Billing;

/// <summary>
/// 定价表：按 (provider, model) 解析 <see cref="ModelPricing"/>，未命中回落 <see cref="Default"/>。
/// 事件信封不携带 provider/model（见 E2），成本引擎按调用方显式指定或默认定价计价。
/// 并发：注册与解析线程安全；<see cref="Entries"/> 为注册时重建的不可变快照（读无锁）。
/// </summary>
public sealed class PricingTable
{
    private readonly object _gate = new();
    private readonly Dictionary<string, ModelPricing> _byKey = new(StringComparer.Ordinal);
    private volatile IReadOnlyDictionary<string, ModelPricing> _snapshot =
        new ReadOnlyDictionary<string, ModelPricing>(new Dictionary<string, ModelPricing>(StringComparer.Ordinal));

    public PricingTable(ResourcePricing resources, ModelPricing? defaultPricing = null)
    {
        Resources = resources ?? ResourcePricing.Zero;
        Default = defaultPricing;
    }

    /// <summary>计算资源单价。</summary>
    public ResourcePricing Resources { get; }

    /// <summary>未命中时的回落定价（可为 null 表示不提供默认模型）。</summary>
    public ModelPricing? Default { get; }

    /// <summary>定价快照（注册时重建；读路径无锁、无分配）。</summary>
    public IReadOnlyDictionary<string, ModelPricing> Entries => _snapshot;

    /// <summary>注册一个模型定价（同 key 覆盖）。</summary>
    public PricingTable Register(ModelPricing pricing)
    {
        ArgumentNullException.ThrowIfNull(pricing);
        lock (_gate)
        {
            _byKey[Key(pricing.Provider, pricing.Model)] = pricing;
            _snapshot = new ReadOnlyDictionary<string, ModelPricing>(
                new Dictionary<string, ModelPricing>(_byKey, StringComparer.Ordinal));
        }
        return this;
    }

    /// <summary>解析定价：精确匹配 &gt; 默认回落；两者皆缺抛 KeyNotFound（错误在边界抛出，不做静默兜底）。</summary>
    public ModelPricing Resolve(string provider, string model)
    {
        if (_snapshot.TryGetValue(Key(provider, model), out var hit))
            return hit;
        if (Default is not null)
            return Default;
        throw new KeyNotFoundException($"未配置模型定价: {provider}/{model}，且无默认定价。");
    }

    private static string Key(string provider, string model) => $"{provider}\u001f{model}";
}
