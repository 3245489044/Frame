namespace Modules.Parsing.Keys;

/// <summary>
/// 文法注册表（**键空间消歧唯一入口**）：<c>GrammarId → KeyGrammar</c>，并按族前缀对键做反解归属。
/// <para>
/// 消歧纪律（对齐 PlacementResolver L-2「多解即错，禁静默择一」）：候选唯一 ⇒ 命中；
/// 多候选但最长族前缀唯一 ⇒ 命中；最长族前缀并列 ⇒ 诊断 Error + 不命中（绝不择一）。
/// </para>
/// <para>装配期写入、运行期只读（无进程静态；实例由宿主注入）。</para>
/// </summary>
public sealed class KeyGrammarRegistry
{
    private readonly Dictionary<string, KeyGrammar> _byId = new(StringComparer.Ordinal);
    private readonly List<KeyGrammar> _ordered = new();
    private readonly IKeyGrammarDiagnostics _diagnostics;

    public KeyGrammarRegistry(IKeyGrammarDiagnostics? diagnostics = null)
        => _diagnostics = diagnostics ?? NullKeyGrammarDiagnostics.Instance;

    /// <summary>已注册文法数（正对照判据：应等于声明文法数）。</summary>
    public int Count => _byId.Count;

    /// <summary>全部文法（序 = 注册序，稳定可比）。</summary>
    public IReadOnlyList<KeyGrammar> All => _ordered.ToArray();

    /// <summary>
    /// 注册文法：同 id 重复即抛（装配期缺陷）；族前缀与既有文法冲突（同前缀且不可区分）⇒ 诊断 Error，
    /// 仍注册以便消歧阶段可见化（不静默丢弃声明）。
    /// </summary>
    public void Register(KeyGrammar grammar)
    {
        ArgumentNullException.ThrowIfNull(grammar);

        if (!_byId.TryAdd(grammar.GrammarId, grammar))
        {
            throw new InvalidOperationException($"键文法重复注册: {grammar.GrammarId}");
        }

        foreach (var existing in _ordered)
        {
            if (existing.FamilyPrefix.Length > 0 &&
                string.Equals(existing.FamilyPrefix, grammar.FamilyPrefix, StringComparison.Ordinal) &&
                !existing.IsDistinguishableFrom(grammar))
            {
                _diagnostics.Report(new KeyGrammarDiagnostic(
                    "key.grammar-prefix-conflict",
                    KeyGrammarSeverity.Error,
                    $"族前缀 '{grammar.FamilyPrefix}' 上存在不可区分文法（键空间重叠）",
                    $"{existing.GrammarId}|{grammar.GrammarId}"));
            }
        }

        _ordered.Add(grammar);
    }

    /// <summary>按 id 取文法。</summary>
    public bool TryGet(string grammarId, out KeyGrammar grammar)
    {
        if (_byId.TryGetValue(grammarId, out var found))
        {
            grammar = found;
            return true;
        }

        grammar = null!;
        return false;
    }

    /// <summary>按键归属文法（最长族前缀优先；并列 ⇒ 诊断 + false）。</summary>
    public bool TryResolve(string? key, out KeyGrammar grammar)
    {
        grammar = null!;
        if (string.IsNullOrEmpty(key))
        {
            _diagnostics.Report(new KeyGrammarDiagnostic(
                "key.grammar-unresolved",
                KeyGrammarSeverity.Warning,
                "空键无法归属文法",
                key ?? string.Empty));
            return false;
        }

        var candidates = new List<KeyGrammar>();
        foreach (var candidate in _ordered)
        {
            if (candidate.IsFamily(key))
            {
                candidates.Add(candidate);
            }
        }

        if (candidates.Count == 0)
        {
            _diagnostics.Report(new KeyGrammarDiagnostic(
                "key.grammar-unresolved",
                KeyGrammarSeverity.Warning,
                "键未命中任何已注册文法的族前缀",
                key));
            return false;
        }

        if (candidates.Count > 1)
        {
            var longest = candidates.Max(static item => item.FamilyPrefix.Length);
            candidates = candidates.Where(item => item.FamilyPrefix.Length == longest).ToList();
        }

        if (candidates.Count > 1)
        {
            _diagnostics.Report(new KeyGrammarDiagnostic(
                "key.grammar-ambiguous",
                KeyGrammarSeverity.Error,
                "键归属多解（最长族前缀并列）：禁择一，请补声明族前缀以消歧",
                key));
            return false;
        }

        grammar = candidates[0];
        return true;
    }

    /// <summary>归属 + 反解（含存量形态；命中形态经 <paramref name="matchedTemplate"/> 可见化）。</summary>
    public bool TryParse(
        string? key,
        out KeyGrammar grammar,
        out KeyValues values,
        out string matchedTemplate)
    {
        if (!TryResolve(key, out grammar))
        {
            values = KeyValues.Empty;
            matchedTemplate = string.Empty;
            return false;
        }

        if (grammar.TryParseAny(key, out values, out matchedTemplate))
        {
            return true;
        }

        _diagnostics.Report(new KeyGrammarDiagnostic(
            "key.grammar-mismatch",
            KeyGrammarSeverity.Warning,
            $"键命中族 '{grammar.FamilyPrefix}' 但不合该文法模板",
            $"{grammar.GrammarId}|{key}"));
        values = KeyValues.Empty;
        matchedTemplate = string.Empty;
        return false;
    }
}
