using Plate.DslMethod.Model;

namespace Usharp.Syntax;

/// <summary>键面声明：<c>surface &lt;family&gt;/&lt;surface&gt; | templates=a,b | prefix=x | readOnly=c</c>。</summary>
public sealed record UsharpKeySurfaceDecl(
    string FamilyId, string Surface, IReadOnlyList<string> Templates,
    string? FamilyPrefix, IReadOnlyList<string> ReadOnlyTemplates);

/// <summary>键轴声明：<c>axis &lt;family&gt;/&lt;name&gt; | shape=text | required=true | allowed=a,b</c>。</summary>
public sealed record UsharpKeyAxisDecl(
    string FamilyId, string Name, string Shape, bool Required,
    IReadOnlyList<string> AllowedValues, IReadOnlyList<string> DisallowInfix);

/// <summary>键族声明：身份模板 + 面 + 轴 + 别名 + 派生前缀。</summary>
public sealed record UsharpKeyFamilyDecl(
    string Id, string Template, string? FamilyPrefix, string? Relation, string? Cardinality,
    string? BusinessKeyTemplate, IReadOnlyList<string> Aliases, IReadOnlyList<string> OrderKeys,
    IReadOnlyList<string> IdentityKeys, string? DomainId, IReadOnlyList<string> DerivedPrefixes,
    IReadOnlyDictionary<string, string> DerivedPrefixRoles,
    IReadOnlyList<UsharpKeySurfaceDecl> Surfaces, IReadOnlyList<UsharpKeyAxisDecl> Axes);

/// <summary>键端口声明：<c>port &lt;name&gt; | direction=In | family=&lt;id&gt; | required=true</c>。</summary>
public sealed record UsharpKeyPortDecl(
    string Name, string Direction, string FamilyId, bool Required,
    string? DefaultValue, string? Cardinality);

/// <summary>空集策略声明：<c>policy &lt;key&gt; | value=CreateMember</c>。</summary>
public sealed record UsharpKeyPolicyDecl(string Key, string Value);

/// <summary>回退链声明：<c>derive &lt;name&gt; | chain=a,b,c</c>。</summary>
public sealed record UsharpKeyDeriveDecl(string Name, IReadOnlyList<string> Chain);

/// <summary>
/// <c>[KeySpace] Name: domains=a,b</c> 语法视图（键声明面；扁平续行，一行一事实）。
/// </summary>
public sealed record UsharpKeySpaceDecl(
    string Id, IReadOnlyList<string> Domains,
    IReadOnlyList<UsharpKeyFamilyDecl> Families,
    IReadOnlyList<UsharpKeyPortDecl> Ports,
    IReadOnlyList<UsharpKeyPolicyDecl> Policies,
    IReadOnlyList<UsharpKeyDeriveDecl> Derives,
    IReadOnlyList<string> UnparsedLines,
    string SourceFile, int LineNumber)
{
    /// <summary>投影单条 <c>[KeySpace]</c>（无名 ⇒ null）。</summary>
    public static UsharpKeySpaceDecl? From(DslDeclaration d)
    {
        if (string.IsNullOrWhiteSpace(d.Name)) return null;

        var domains = SplitList(d.Field("domains"));
        var families = new List<FamilyBuilder>();
        var byFamily = new Dictionary<string, FamilyBuilder>(StringComparer.Ordinal);
        var ports = new List<UsharpKeyPortDecl>();
        var policies = new List<UsharpKeyPolicyDecl>();
        var derives = new List<UsharpKeyDeriveDecl>();
        var unparsed = new List<string>();

        FamilyBuilder Family(string id)
        {
            if (byFamily.TryGetValue(id, out var existing)) return existing;
            var created = new FamilyBuilder(id);
            byFamily[id] = created;
            families.Add(created);
            return created;
        }

        foreach (var raw in d.Continuation)
        {
            var line = (raw ?? string.Empty).Trim();
            if (line.Length == 0) continue;

            var parts = SplitParts(line);
            var head = parts[0];
            var fields = ParseFields(parts, 1);

            if (TryHead(head, "family", out var familyId))
            {
                var family = Family(familyId);
                family.Template = Get(fields, "template") ?? family.Template;
                family.FamilyPrefix = Get(fields, "prefix") ?? family.FamilyPrefix;
                family.Relation = Get(fields, "relation") ?? family.Relation;
                family.Cardinality = Get(fields, "cardinality") ?? family.Cardinality;
                family.BusinessKeyTemplate = Get(fields, "businessKey") ?? family.BusinessKeyTemplate;
                family.DomainId = Get(fields, "domain") ?? family.DomainId;
                family.Aliases.AddRange(SplitList(Get(fields, "aliases")));
                family.OrderKeys.AddRange(SplitList(Get(fields, "orderKeys")));
                family.IdentityKeys.AddRange(SplitList(Get(fields, "identity")));
                family.DerivedPrefixes.AddRange(SplitList(Get(fields, "derivedPrefixes")));
                continue;
            }

            if (TryHead(head, "surface", out var surfaceRef))
            {
                var (family, name) = SplitRef(surfaceRef);
                if (family.Length == 0 || name.Length == 0) { unparsed.Add(line); continue; }
                Family(family).Surfaces.Add(new UsharpKeySurfaceDecl(
                    family, name, SplitList(Get(fields, "templates")),
                    Get(fields, "prefix"), SplitList(Get(fields, "readOnly"))));
                continue;
            }

            if (TryHead(head, "axis", out var axisRef))
            {
                var (family, name) = SplitRef(axisRef);
                if (family.Length == 0 || name.Length == 0) { unparsed.Add(line); continue; }
                Family(family).Axes.Add(new UsharpKeyAxisDecl(
                    family, name, Get(fields, "shape") ?? "text",
                    !string.Equals(Get(fields, "required"), "false", StringComparison.OrdinalIgnoreCase),
                    SplitList(Get(fields, "allowed")), SplitList(Get(fields, "disallow"))));
                continue;
            }

            if (TryHead(head, "alias", out var aliasRef))
            {
                var (family, _) = SplitRef(aliasRef);
                var template = Get(fields, "template") ?? Get(fields, "templates");
                if (family.Length == 0 || string.IsNullOrWhiteSpace(template)) { unparsed.Add(line); continue; }
                Family(family).Aliases.AddRange(SplitList(template));
                continue;
            }

            if (TryHead(head, "derived", out var derivedRef))
            {
                var eq = derivedRef.IndexOf('=');
                if (eq <= 0) { unparsed.Add(line); continue; }
                var familyRole = derivedRef[..eq].Trim();
                var prefix = derivedRef[(eq + 1)..].Trim();
                var slash = familyRole.IndexOf('/');
                if (slash <= 0 || slash >= familyRole.Length - 1 || prefix.Length == 0) { unparsed.Add(line); continue; }
                var builder = Family(familyRole[..slash]);
                builder.DerivedPrefixes.Add(prefix);
                builder.DerivedPrefixRoles[familyRole[(slash + 1)..]] = prefix;
                continue;
            }

            if (TryHead(head, "port", out var portName))
            {
                ports.Add(new UsharpKeyPortDecl(
                    portName, Get(fields, "direction") ?? "In", Get(fields, "family") ?? string.Empty,
                    !string.Equals(Get(fields, "required"), "false", StringComparison.OrdinalIgnoreCase),
                    Get(fields, "default"), Get(fields, "cardinality")));
                continue;
            }

            if (TryHead(head, "policy", out var policyKey))
            {
                var value = Get(fields, "value");
                if (string.IsNullOrWhiteSpace(value)) { unparsed.Add(line); continue; }
                policies.Add(new UsharpKeyPolicyDecl(policyKey, value));
                continue;
            }

            if (TryHead(head, "derive", out var deriveName))
            {
                var chain = SplitList(Get(fields, "chain"));
                if (chain.Count == 0) { unparsed.Add(line); continue; }
                derives.Add(new UsharpKeyDeriveDecl(deriveName, chain));
                continue;
            }

            unparsed.Add(line);
        }

        return new UsharpKeySpaceDecl(
            d.Name.Trim(), domains, families.Select(f => f.Build()).ToList(),
            ports, policies, derives, unparsed, d.SourceFile, d.LineNumber);
    }

    private sealed class FamilyBuilder
    {
        public FamilyBuilder(string id) => Id = id;
        public string Id { get; }
        public string Template { get; set; } = string.Empty;
        public string? FamilyPrefix { get; set; }
        public string? Relation { get; set; }
        public string? Cardinality { get; set; }
        public string? BusinessKeyTemplate { get; set; }
        public string? DomainId { get; set; }
        public List<string> Aliases { get; } = new();
        public List<string> OrderKeys { get; } = new();
        public List<string> IdentityKeys { get; } = new();
        public List<string> DerivedPrefixes { get; } = new();
        public Dictionary<string, string> DerivedPrefixRoles { get; } = new(StringComparer.Ordinal);
        public List<UsharpKeySurfaceDecl> Surfaces { get; } = new();
        public List<UsharpKeyAxisDecl> Axes { get; } = new();

        public UsharpKeyFamilyDecl Build() => new(
            Id, Template, FamilyPrefix, Relation, Cardinality, BusinessKeyTemplate,
            Aliases, OrderKeys, IdentityKeys, DomainId, DerivedPrefixes, DerivedPrefixRoles, Surfaces, Axes);
    }

    internal static List<string> SplitParts(string line)
        => line.Split('|', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    internal static Dictionary<string, string> ParseFields(IReadOnlyList<string> parts, int skip)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = skip; i < parts.Count; i++)
        {
            var eq = parts[i].IndexOf('=');
            if (eq <= 0) continue;
            map[parts[i][..eq].Trim()] = parts[i][(eq + 1)..].Trim();
        }
        return map;
    }

    internal static string? Get(IReadOnlyDictionary<string, string> map, string key)
        => map.TryGetValue(key, out var value) && value.Length > 0 ? value : null;

    internal static List<string> SplitList(string? raw)
        => string.IsNullOrWhiteSpace(raw)
            ? new List<string>()
            : raw.Split(new[] { ',', ';' }, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).ToList();

    internal static bool TryHead(string head, string marker, out string rest)
    {
        rest = string.Empty;
        if (!head.StartsWith(marker, StringComparison.Ordinal)) return false;
        if (head.Length == marker.Length) return true;
        if (!char.IsWhiteSpace(head[marker.Length])) return false;
        rest = head[marker.Length..].Trim();
        return rest.Length > 0;
    }

    internal static string? NullIfEmpty(string? value)
        => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    internal static (string Family, string Name) SplitRef(string text)
    {
        var slash = text.IndexOf('/');
        return slash <= 0 ? (string.Empty, string.Empty)
            : (text[..slash].Trim(), text[(slash + 1)..].Trim());
    }
}
