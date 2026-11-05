namespace Modules.Compute.Traversal;

/// <summary>
/// 名称 → 实体索引（唯一实现）：大小写不敏感、可配置同名策略、可分配唯一名。
/// <para>纪律：同名策略单源（缺省先序保留）；调用方禁再自建 <c>Dictionary&lt;string, …&gt;(StringComparer.OrdinalIgnoreCase)</c>。</para>
/// </summary>
/// <typeparam name="T">实体类型（引用类型）。</typeparam>
public sealed class NameIndex<T> where T : class
{
    private readonly Dictionary<string, T> _map;
    private readonly List<string> _order = new();
    private readonly Dictionary<string, int> _uniqueCounters = new(StringComparer.OrdinalIgnoreCase);
    private readonly bool _allowOverwrite;

    /// <param name="allowOverwrite">同名是否后者覆盖（缺省 false = 先序保留）。</param>
    public NameIndex(bool allowOverwrite = false)
    {
        _allowOverwrite = allowOverwrite;
        _map = new Dictionary<string, T>(StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>条目数。</summary>
    public int Count => _map.Count;

    /// <summary>登记（空名/空值跳过）；同名按构造策略处置。</summary>
    /// <param name="name">名称。</param>
    /// <param name="value">实体。</param>
    /// <returns>是否发生写入。</returns>
    public bool TryAdd(string? name, T value)
    {
        if (string.IsNullOrEmpty(name) || value is null) return false;

        if (_map.TryGetValue(name, out _))
        {
            if (!_allowOverwrite) return false;
            _map[name] = value;
            return true;
        }

        _map[name] = value;
        _order.Add(name);
        return true;
    }

    /// <summary>按键取值（不改变登记序）。</summary>
    /// <param name="name">名称。</param>
    /// <param name="value">命中的实体。</param>
    public bool TryGet(string? name, out T? value)
    {
        value = null;
        return !string.IsNullOrEmpty(name) && _map.TryGetValue(name, out value);
    }

    /// <summary>键是否存在。</summary>
    /// <param name="name">名称。</param>
    public bool ContainsKey(string? name)
        => !string.IsNullOrEmpty(name) && _map.ContainsKey(name!);

    /// <summary>全部实体（插入序；供类型过滤等只读遍历）。</summary>
    public IReadOnlyCollection<T> Values
        => _order.Where(_map.ContainsKey).Select(key => _map[key]).ToArray();

    /// <summary>移除键（大小写不敏感；同步移除唯一名计数）。</summary>
    /// <param name="name">名称。</param>
    public bool Remove(string? name)
    {
        if (string.IsNullOrEmpty(name)) return false;
        if (!_map.Remove(name!)) return false;

        var position = _order.FindIndex(key => string.Equals(key, name, StringComparison.OrdinalIgnoreCase));
        if (position >= 0) _order.RemoveAt(position);
        _uniqueCounters.Remove(name!);
        return true;
    }

    /// <summary>
    /// 分配唯一名（同名追加 <c>_1</c>/<c>_2</c>…；不登记映射）。
    /// <para>调用方按需把基础名/唯一名映射到实体（保持既有先序保留语义）。</para>
    /// </summary>
    /// <param name="baseName">基础名。</param>
    /// <param name="separator">后缀分隔符。</param>
    public string NextUnique(string? baseName, string separator = "_")
    {
        var name = string.IsNullOrEmpty(baseName) ? string.Empty : baseName!;
        if (name.Length == 0)
            throw new ArgumentException("基础名不得为空。", nameof(baseName));

        if (!_map.ContainsKey(name) && !_uniqueCounters.ContainsKey(name))
        {
            _uniqueCounters[name] = 1;
            return name;
        }

        _uniqueCounters.TryGetValue(name, out var count);
        if (count <= 0) count = 1;
        string candidate;
        do
        {
            candidate = name + separator + count;
            count++;
        }
        while (_map.ContainsKey(candidate));

        _uniqueCounters[name] = count;
        return candidate;
    }
}
