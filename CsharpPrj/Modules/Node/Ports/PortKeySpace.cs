namespace Modules.Node.Ports;

/// <summary>端口条目（值泛型：JSON 文本 / 宿主原生值共用同一键空间规则，规则不随值形态分叉）。</summary>
public record PortEntry<TValue>(string PortKey, string NodeKey, string Port, TValue Value, bool Bare);

/// <summary>JSON 文本值形态的端口条目（既有契约，保留兼容）。</summary>
public sealed record PortEntry : PortEntry<string>
{
    public PortEntry(string PortKey, string NodeKey, string Port, string Value, bool Bare)
        : base(PortKey, NodeKey, Port, Value, Bare)
    {
    }
}

/// <summary>
/// 端口键空间（G2 单一实现，读写共用；值泛型 = 值缝 V1）：
/// <list type="bullet">
/// <item><c>{node}.{port}</c> 恒有（跨节点互不覆盖）；</item>
/// <item>裸 <c>{port}</c> 仅在未被任一更早节点占用时并入（先到者胜，防跨节点互覆）；</item>
/// <item>null 值不入源；源键集与溯源集恒一致。</item>
/// </list>
/// 只处理键与值（值形态由泛型参数决定，规则只有一份），不写库、不决定读哪些位置。
/// </summary>
public class PortKeySpace<TValue>
{
    private readonly Dictionary<string, PortEntry<TValue>> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _bareOwners = new(StringComparer.Ordinal);

    public int Count => _entries.Count;

    /// <summary>定向端口：<c>{node}.{port}</c>（恒有）。</summary>
    public bool TryAdd(string nodeKey, string port, TValue value)
    {
        if (string.IsNullOrEmpty(nodeKey) || string.IsNullOrEmpty(port) || value is null)
        {
            return false;
        }

        var key = BuildPortKey(nodeKey, port);
        _entries[key] = new PortEntry<TValue>(key, nodeKey, port, value, Bare: false);
        return true;
    }

    /// <summary>裸端口：未被任一更早节点占用时并入（先到者胜）。</summary>
    public bool TryAddBare(string nodeKey, string port, TValue value)
    {
        if (string.IsNullOrEmpty(nodeKey) || string.IsNullOrEmpty(port) || value is null)
        {
            return false;
        }

        if (_bareOwners.ContainsKey(port))
        {
            return false;
        }

        _bareOwners[port] = nodeKey;
        _entries[port] = new PortEntry<TValue>(port, nodeKey, port, value, Bare: true);
        return true;
    }

    /// <summary>
    /// 既有键的接纳回放：把外部已存在的键纳入同一判定（裸键登记为已占用）。
    /// 仅登记占位（值未知，<see cref="TryGet"/> 取到 default），用途 = 准入判定，不用于取值。
    /// </summary>
    public void Adopt(string portKey, string? owner = null)
    {
        if (string.IsNullOrEmpty(portKey) || _entries.ContainsKey(portKey))
        {
            return;
        }

        var separator = portKey.LastIndexOf('.');
        if (separator > 0 && separator < portKey.Length - 1)
        {
            _entries[portKey] = new PortEntry<TValue>(
                portKey,
                portKey[..separator],
                portKey[(separator + 1)..],
                default!,
                Bare: false);
            return;
        }

        _bareOwners[portKey] = owner ?? string.Empty;
        _entries[portKey] = new PortEntry<TValue>(portKey, owner ?? string.Empty, portKey, default!, Bare: true);
    }

    public bool IsBareOccupied(string port) => _bareOwners.ContainsKey(port);

    public string? BareOwner(string port) => _bareOwners.TryGetValue(port, out var owner) ? owner : null;

    public bool TryGet(string portKey, out TValue value)
    {
        if (_entries.TryGetValue(portKey, out var entry))
        {
            value = entry.Value;
            return true;
        }

        value = default!;
        return false;
    }

    public IReadOnlyDictionary<string, TValue> Snapshot() =>
        _entries.ToDictionary(pair => pair.Key, pair => pair.Value.Value, StringComparer.Ordinal);

    /// <summary>溯源：端口键 → 产出节点（与源键集恒一致）。</summary>
    public IReadOnlyDictionary<string, string> Provenance() =>
        _entries.ToDictionary(pair => pair.Key, pair => pair.Value.NodeKey, StringComparer.Ordinal);

    public IReadOnlyList<PortEntry<TValue>> Entries => _entries.Values.ToArray();

    public static string BuildPortKey(string nodeKey, string port) => nodeKey + "." + port;
}

/// <summary>
/// 端口键空间（JSON 文本值形态，既有契约）：与 <see cref="PortKeySpace{TValue}"/> 同一实现，仅值形态为 string。
/// </summary>
public sealed class PortKeySpace : PortKeySpace<string>
{
    /// <summary>未命中时返回空串（既有契约；调用方须判返回值）。</summary>
    public new bool TryGet(string portKey, out string value)
    {
        if (base.TryGet(portKey, out var stored))
        {
            value = stored;
            return true;
        }

        value = string.Empty;
        return false;
    }

    public new IReadOnlyList<PortEntry> Entries =>
        base.Entries
            .Select(entry => new PortEntry(entry.PortKey, entry.NodeKey, entry.Port, entry.Value, entry.Bare))
            .ToArray();
}

