namespace Modules.Node.Spec;

/// <summary>轴内标签词表（token ⇔ 位值；位空间不跨轴共享）。</summary>
public interface ITagVocabulary
{
    /// <summary>轴名。</summary>
    string Axis { get; }

    /// <summary>token → 位值（token 大小写按实现归一）。</summary>
    IReadOnlyDictionary<string, int> Marks { get; }
}

/// <summary>词表实现（不可变数据）。</summary>
/// <param name="Axis">轴名。</param>
/// <param name="Marks">token → 位值。</param>
public sealed record TagVocabulary(string Axis, IReadOnlyDictionary<string, int> Marks) : ITagVocabulary
{
    /// <summary>由键值对构造（重复 token 后者胜；空轴 ⇒ 抛）。</summary>
    public static TagVocabulary Create(string axis, IEnumerable<KeyValuePair<string, int>> marks)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(axis);
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var pair in marks ?? Array.Empty<KeyValuePair<string, int>>())
        {
            if (!string.IsNullOrWhiteSpace(pair.Key))
            {
                map[pair.Key] = pair.Value;
            }
        }

        return new TagVocabulary(axis, map);
    }
}

/// <summary>
/// 轴化词表注册表端口（实例注入；禁进程静态）。登记幂等：同轴同映射 = 空操作；
/// 同轴异映射 ⇒ 诊断 + 拒绝覆盖（不静默降级）。
/// </summary>
public interface ITagVocabularyRegistry
{
    /// <summary>是否已冻结。</summary>
    bool IsFrozen { get; }

    /// <summary>已登记轴名。</summary>
    IReadOnlyCollection<string> Axes { get; }

    /// <summary>冲突/拒绝覆盖诊断（只增）。</summary>
    IReadOnlyList<string> Diagnostics { get; }

    /// <summary>登记词表（冻结后 ⇒ 抛）。</summary>
    void Register(ITagVocabulary vocabulary);

    /// <summary>冻结（运行期只读门）。</summary>
    void Freeze();

    /// <summary>取某轴词表。</summary>
    bool TryGet(string axis, out ITagVocabulary vocabulary);

    /// <summary>某轴内 token → 位值（大小写不敏感）。</summary>
    bool TryResolve(string axis, string token, out int bit);
}

/// <summary>标签归一结果（未识别 token 可见化：不静默丢）。</summary>
/// <param name="IsValid">是否全部 token 可识别。</param>
/// <param name="Normalized">规范串（<c>|</c> 连接；无 token ⇒ 空串）。</param>
/// <param name="Bits">主轴位掩码（OR）。</param>
/// <param name="Unknown">未识别 token（原样）。</param>
public sealed record NodeTagSetParseResult(
    bool IsValid,
    string Normalized,
    int Bits,
    IReadOnlyList<string> Unknown);

/// <summary>
/// 标签集归一（分隔符 <c>|</c>/<c>,</c>/<c>;</c> 单源）：token 形如 <c>axis.token</c>，无前缀 ⇒ 指定主轴。
/// <para>未识别 token ⇒ 归一失败并列出（拒绝，不静默丢弃）。</para>
/// </summary>
public static class NodeTagSet
{
    private static readonly char[] Separators = { '|', ',', ';' };

    /// <summary>归一为规范串；含未识别 token ⇒ null。</summary>
    public static string? Normalize(string? tags, ITagVocabularyRegistry registry, string axis)
    {
        var result = Parse(tags, registry, axis);
        return result.IsValid ? result.Normalized : null;
    }

    /// <summary>解析/归一（同时给位掩码与未识别 token）。</summary>
    public static NodeTagSetParseResult Parse(string? tags, ITagVocabularyRegistry registry, string axis)
    {
        ArgumentNullException.ThrowIfNull(registry);
        ArgumentException.ThrowIfNullOrWhiteSpace(axis);

        var text = (tags ?? string.Empty).Trim();
        if (text.Length == 0)
        {
            return new NodeTagSetParseResult(true, string.Empty, 0, Array.Empty<string>());
        }

        var normalized = new List<string>();
        var unknown = new List<string>();
        var bits = 0;

        foreach (var part in text.Split(
                     Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var separator = part.IndexOf('.');
            var hasAxis = separator > 0 && separator < part.Length - 1;
            var tokenAxis = hasAxis ? part[..separator] : axis;
            var local = hasAxis ? part[(separator + 1)..] : part;

            if (!registry.TryResolve(tokenAxis, local, out var bit))
            {
                unknown.Add(part);
                continue;
            }

            var canonical = CanonicalOf(registry, tokenAxis, local);
            var formatted = hasAxis ? tokenAxis + "." + canonical : canonical;
            if (!normalized.Contains(formatted, StringComparer.Ordinal))
            {
                normalized.Add(formatted);
            }

            if (string.Equals(tokenAxis, axis, StringComparison.OrdinalIgnoreCase))
            {
                bits |= bit;
            }
        }

        return new NodeTagSetParseResult(
            unknown.Count == 0,
            string.Join("|", normalized),
            bits,
            unknown);
    }

    /// <summary>是否含 token（集合语义；大小写不敏感）。</summary>
    public static bool Contains(string? tags, string token)
    {
        if (string.IsNullOrWhiteSpace(tags) || string.IsNullOrWhiteSpace(token))
        {
            return false;
        }

        foreach (var part in tags.Split(
                     Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (string.Equals(part, token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            var separator = part.IndexOf('.');
            if (separator > 0 && separator < part.Length - 1 &&
                string.Equals(part[(separator + 1)..], token, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        return false;
    }

    private static string CanonicalOf(ITagVocabularyRegistry registry, string axis, string token)
    {
        if (!registry.TryGet(axis, out var vocabulary))
        {
            return token;
        }

        foreach (var name in vocabulary.Marks.Keys)
        {
            if (string.Equals(name, token, StringComparison.OrdinalIgnoreCase))
            {
                return name;
            }
        }

        return token;
    }
}
