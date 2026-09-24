namespace Modules.Node.Ports;

public sealed record PortEntry(string PortKey, string NodeKey, string Port, string Value, bool Bare);

/// <summary>
/// 端口键空间（G2 单一实现，读写共用）：
/// <list type="bullet">
/// <item><c>{node}.{port}</c> 恒有（跨节点互不覆盖）；</item>
/// <item>裸 <c>{port}</c> 仅在未被任一更早节点占用时并入（先到者胜，防跨节点互覆）；</item>
/// <item>null 值不入源；源键集与溯源集恒一致。</item>
/// </list>
/// 只处理键与字符串值，不写库、不决定读哪些位置。
/// </summary>
public sealed class PortKeySpace
{
    private readonly Dictionary<string, PortEntry> _entries = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _bareOwners = new(StringComparer.Ordinal);

    public int Count => _entries.Count;

    /// <summary>定向端口：<c>{node}.{port}</c>（恒有）。</summary>
    public bool TryAdd(string nodeKey, string port, string? value)
    {
        if (string.IsNullOrEmpty(nodeKey) || string.IsNullOrEmpty(port) || value is null)
        {
            return false;
        }

        var key = BuildPortKey(nodeKey, port);
        _entries[key] = new PortEntry(key, nodeKey, port, value, Bare: false);
        return true;
    }

    /// <summary>裸端口：未被任一更早节点占用时并入（先到者胜）。</summary>
    public bool TryAddBare(string nodeKey, string port, string? value)
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
        _entries[port] = new PortEntry(port, nodeKey, port, value, Bare: true);
        return true;
    }

    public bool IsBareOccupied(string port) => _bareOwners.ContainsKey(port);

    public string? BareOwner(string port) => _bareOwners.TryGetValue(port, out var owner) ? owner : null;

    public bool TryGet(string portKey, out string value)
    {
        if (_entries.TryGetValue(portKey, out var entry))
        {
            value = entry.Value;
            return true;
        }

        value = string.Empty;
        return false;
    }

    public IReadOnlyDictionary<string, string> Snapshot() =>
        _entries.ToDictionary(pair => pair.Key, pair => pair.Value.Value, StringComparer.Ordinal);

    /// <summary>溯源：端口键 → 产出节点（与源键集恒一致）。</summary>
    public IReadOnlyDictionary<string, string> Provenance() =>
        _entries.ToDictionary(pair => pair.Key, pair => pair.Value.NodeKey, StringComparer.Ordinal);

    public IReadOnlyList<PortEntry> Entries => _entries.Values.ToArray();

    public static string BuildPortKey(string nodeKey, string port) => nodeKey + "." + port;
}
