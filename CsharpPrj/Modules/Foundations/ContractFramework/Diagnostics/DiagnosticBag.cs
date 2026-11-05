using ContractFramework.Codes;

namespace ContractFramework.Diagnostics;

/// <summary>诊断条目（码 + 键 + 可选细节）：<c>code:key[:detail]</c> 即唯一出口外形。</summary>
public readonly record struct DiagnosticItem(string Code, string Key, string? Detail = null)
{
    /// <inheritdoc />
    public override string ToString()
    {
        if (string.IsNullOrEmpty(Key)) return Code;
        return Detail is null ? $"{Code}:{Key}" : $"{Code}:{Key}:{Detail}";
    }
}

/// <summary>
/// 诊断出口（唯一实现）：受控码校验 + 去重码序 + 单一投影（<see cref="ToLines"/>）。
/// <para>纪律：码必须 ∈ <see cref="CodeSet"/>（构造时提供）；消费方只 Add，不自建 List&lt;string&gt;。</para>
/// </summary>
public sealed class DiagnosticBag
{
    private readonly CodeSet? _codes;
    private readonly object _gate = new();
    private readonly List<DiagnosticItem> _items = new();
    private readonly List<string> _codesSeen = new();
    private readonly HashSet<string> _codeSet = new(StringComparer.Ordinal);

    /// <param name="codes">受控码集；提供后 <see cref="Add"/> 对未知码抛（fail-fast）。</param>
    public DiagnosticBag(CodeSet? codes = null) => _codes = codes;

    /// <summary>条目快照（追加序）。</summary>
    public IReadOnlyList<DiagnosticItem> Items
    {
        get { lock (_gate) return _items.ToArray(); }
    }

    /// <summary>去重码序（首现序）。</summary>
    public IReadOnlyList<string> Codes
    {
        get { lock (_gate) return _codesSeen.ToArray(); }
    }

    /// <summary>当前条目数。</summary>
    public int Count
    {
        get { lock (_gate) return _items.Count; }
    }

    /// <summary>登记一条诊断（码受 <see cref="CodeSet"/> 校验）。</summary>
    /// <param name="code">受控码。</param>
    /// <param name="key">主体键（空 ⇒ 只输出码）。</param>
    /// <param name="detail">可选细节。</param>
    public void Add(string code, string key, string? detail = null)
    {
        var normalized = _codes is null ? (code ?? string.Empty) : _codes.Require(code);
        var item = new DiagnosticItem(normalized, key ?? string.Empty, detail);
        lock (_gate)
        {
            _items.Add(item);
            if (_codeSet.Add(normalized)) _codesSeen.Add(normalized);
        }
    }

    /// <summary>批量合并既有诊断行（<c>code:key[:detail]</c>；用于委托下游结果）。</summary>
    /// <param name="lines">诊断行集合。</param>
    public void AddRange(IEnumerable<string>? lines)
    {
        if (lines is null) return;
        foreach (var line in lines)
        {
            if (string.IsNullOrWhiteSpace(line)) continue;
            var parts = line.Split(':', 3);
            Add(parts[0], parts.Length > 1 ? parts[1] : string.Empty, parts.Length > 2 ? parts[2] : null);
        }
    }

    /// <summary>是否已登记某码。</summary>
    /// <param name="code">待查询码。</param>
    public bool HasCode(string code)
    {
        lock (_gate) return _codeSet.Contains(code ?? string.Empty);
    }

    /// <summary>唯一投影出口：<c>code:key[:detail]</c> 行集合（消费端零改动的字符串外形）。</summary>
    public IReadOnlyList<string> ToLines()
    {
        lock (_gate) return _items.Select(item => item.ToString()).ToArray();
    }
}
