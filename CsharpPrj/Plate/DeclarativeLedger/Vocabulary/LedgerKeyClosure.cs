namespace BasicMethod.Declarative.Ledger;

/// <summary>
/// 分层键闭包：层名 → 合法键集（「未知键 fail-fast」的唯一实现）。
/// <para>
/// 消费方（读取器 / 结构门 / 离线校验）按层调用 <see cref="EnsureKnown"/>；违规文案由调用方注入
/// （域文案留消费方）。未登记的层**不判**（不追加），与「只登记已知层」的宽松口径一致。
/// </para>
/// </summary>
public sealed class LedgerKeyClosure
{
    private readonly Dictionary<string, HashSet<string>> _byLayer;

    /// <param name="layers">层集（层名 → 键集；层名重复即抛）。</param>
    public LedgerKeyClosure(IEnumerable<(string Layer, IReadOnlyList<string> Keys)> layers)
    {
        ArgumentNullException.ThrowIfNull(layers);

        _byLayer = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var (layer, keys) in layers)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(layer);
            ArgumentNullException.ThrowIfNull(keys);
            if (!_byLayer.TryAdd(layer, new HashSet<string>(keys, StringComparer.Ordinal)))
                throw new ArgumentException($"层重复登记：'{layer}'", nameof(layers));
        }

        Layers = [.. _byLayer.Keys.OrderBy(key => key, StringComparer.Ordinal)];
    }

    /// <summary>已登记层名（Ordinal 升序）。</summary>
    public IReadOnlyList<string> Layers { get; }

    /// <summary>层是否已登记（未登记层不判）。</summary>
    public bool IsKnownLayer(string layer) => layer is not null && _byLayer.ContainsKey(layer);

    /// <summary>层键集（未登记层 → 空集；键 Ordinal 升序）。</summary>
    public IReadOnlyList<string> KeysOf(string layer)
        => _byLayer.TryGetValue(layer, out var keys)
            ? [.. keys.OrderBy(key => key, StringComparer.Ordinal)]
            : [];

    /// <summary>
    /// 收集**未登记键**（保持出现序、去重）；<paramref name="messageFactory"/>(path, key) → 文案。
    /// 未登记层 → 不判（不追加）。
    /// </summary>
    public void EnsureKnown(
        string layer,
        IEnumerable<string> presentKeys,
        string path,
        Func<string, string, string> messageFactory,
        List<string> violations)
    {
        ArgumentNullException.ThrowIfNull(presentKeys);
        ArgumentNullException.ThrowIfNull(messageFactory);
        ArgumentNullException.ThrowIfNull(violations);

        if (layer is null || !_byLayer.TryGetValue(layer, out var known)) return;

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var key in presentKeys)
        {
            if (key is null || known.Contains(key) || !seen.Add(key)) continue;

            violations.Add(messageFactory(path, key));
        }
    }
}
