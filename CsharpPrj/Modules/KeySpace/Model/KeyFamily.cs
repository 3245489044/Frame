namespace Modules.KeySpace.Model;

/// <summary>键与实体的关系（声明面给出；框架不猜）。</summary>
public enum KeyRelation { Self, Container, Member, Peer, Resource, External, Unknown }

/// <summary>键的基数。</summary>
public enum KeyCardinality { One, ZeroOrOne, Many }

/// <summary>
/// 键族声明：身份模板 + 关系 + 基数 + 别名。模板文法复用 Modules.Parsing.Keys。
/// </summary>
public sealed record KeyFamily(
    string Id,
    string Template,
    string? FamilyPrefix,
    KeyRelation Relation,
    KeyCardinality Cardinality,
    IReadOnlyList<string> Aliases,
    IReadOnlyList<string>? OrderKeys = null,
    string? BusinessKeyTemplate = null,
    IReadOnlyList<string>? DerivedPrefixes = null,
    KeyDomainId? DomainId = null,
    IReadOnlyList<string>? IdentityKeys = null,
    IReadOnlyDictionary<KeySurface, KeySurfaceSpec>? Surfaces = null,
    IReadOnlyDictionary<string, string>? DerivedPrefixRoles = null,
    IReadOnlyList<KeyAxisSpec>? Axes = null,
    IReadOnlyDictionary<string, bool>? DerivedPrefixReversibility = null,
    IReadOnlyList<CoordinateRuleSet>? CoordinateRules = null,
    bool? Authority = null,
    string? KindLexicon = null);

/// <summary>
/// 实体：同一逻辑对象的所有称呼（别名）。判等由 IKeyEquivalence 消费本集合完成，实体本身不持有规则。
/// </summary>
public sealed record KeyEntity(string Id, IReadOnlyList<KeyRef> Aliases)
{
    public static KeyEntity Single(string id, KeyRef self) => new(id, new[] { self });

    public bool Contains(KeyRef key)
        => Aliases.Any(alias => alias.Equals(key)) || string.Equals(key.Value, Id, StringComparison.Ordinal);
}
