namespace BasicMethod.Declarative.Ledger;

/// <summary>
/// 具名词表：一个**命名**的有序词序列（声明面枚举的唯一 C# 源）。
/// <para>
/// 纪律（SingleImplementation）：同一枚举只在本实例定义一次；结构门的 <c>enum</c>、
/// 执行分支、异常文案的词表段一律**派生**（<see cref="Render"/> / <see cref="RenderJsonArray"/>），
/// 禁第二份数据。词序 = 入参序（渲染文案逐字稳定，禁重排）。
/// </para>
/// </summary>
public sealed class LedgerTerm
{
    /// <param name="name">词表名（登记 / 诊断用；不参与渲染）。</param>
    /// <param name="words">词序列（权威序；重复词即抛）。</param>
    public LedgerTerm(string name, IReadOnlyList<string> words)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(words);

        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var word in words)
        {
            ArgumentException.ThrowIfNullOrWhiteSpace(word);
            if (!seen.Add(word))
                throw new ArgumentException($"词表 '{name}' 含重复词 '{word}'（词表须唯一）", nameof(words));
        }

        Name = name;
        Words = [.. words];
    }

    /// <summary>词表名（登记 / 诊断用）。</summary>
    public string Name { get; }

    /// <summary>词序列（权威序）。</summary>
    public IReadOnlyList<string> Words { get; }

    /// <summary>严格成员判定（空串不视为成员）。</summary>
    public bool Contains(string word)
    {
        ArgumentNullException.ThrowIfNull(word);
        return Words.Contains(word, StringComparer.Ordinal);
    }

    /// <summary>登记判定；<paramref name="allowEmpty"/> = 空串是否视为「无声明」（合法）。</summary>
    public bool IsKnown(string? word, bool allowEmpty = false)
        => string.IsNullOrEmpty(word) ? allowEmpty : Contains(word);

    /// <summary>渲染为竖线文案（<c>a|b|c</c>）：异常文案词表段的唯一口径。</summary>
    public string Render() => string.Join("|", Words);

    /// <summary>渲染为 JSON 数组字面量：结构门 <c>enum</c> 写回的唯一口径（词序不变）。</summary>
    public string RenderJsonArray()
        => "[" + string.Join(", ", Words.Select(word => "\"" + word + "\"")) + "]";

    /// <summary>按序合成新词表（并集须显式命名；重复词即抛，与主构造同判）。</summary>
    public static LedgerTerm Compose(string name, params LedgerTerm[] parts)
    {
        ArgumentNullException.ThrowIfNull(parts);

        var words = new List<string>();
        foreach (var part in parts)
        {
            ArgumentNullException.ThrowIfNull(part);
            words.AddRange(part.Words);
        }

        return new LedgerTerm(name, words);
    }

    /// <summary>单值词表（如「依赖直投」这类只取一值的具名标记）。</summary>
    public static LedgerTerm Single(string name, string word) => new(name, [word]);
}

/// <summary>
/// 词表册：命名词表的唯一登记点（重名即抛）。消费方按名取用（<see cref="Require"/>），禁各持副本。
/// <para>
/// 机制半不含任何领域词：词表内容（名字与词）全部由消费方注入；本册只承担「登记 + 命名唯一 + 按名取用」。
/// </para>
/// </summary>
public sealed class LedgerVocabulary
{
    private readonly Dictionary<string, LedgerTerm> _byName;

    /// <param name="terms">词表集（登记序无关；重名即抛）。</param>
    public LedgerVocabulary(IEnumerable<LedgerTerm> terms)
    {
        ArgumentNullException.ThrowIfNull(terms);

        _byName = new Dictionary<string, LedgerTerm>(StringComparer.Ordinal);
        foreach (var term in terms)
        {
            ArgumentNullException.ThrowIfNull(term);
            if (!_byName.TryAdd(term.Name, term))
                throw new ArgumentException($"词表重复登记：'{term.Name}'（命名单源被破坏）", nameof(terms));
        }

        Terms = [.. _byName.Values.OrderBy(term => term.Name, StringComparer.Ordinal)];
    }

    /// <summary>全部词表（名 Ordinal 升序）。</summary>
    public IReadOnlyList<LedgerTerm> Terms { get; }

    /// <summary>按名取词表；未登记即抛（含已登记清单，防拼写漂移静默）。</summary>
    public LedgerTerm Require(string name)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(name);

        return _byName.TryGetValue(name, out var term)
            ? term
            : throw new DeclarationViolationException(
                $"词表 '{name}' 未登记。已登记：{string.Join(", ", _byName.Keys.OrderBy(key => key, StringComparer.Ordinal))}");
    }

    /// <summary>按名取词表（未登记 → null）。</summary>
    public LedgerTerm? Find(string name) => name is null ? null : _byName.GetValueOrDefault(name);
}
