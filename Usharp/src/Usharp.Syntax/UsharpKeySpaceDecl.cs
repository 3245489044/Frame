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

        var domains = UsharpLineReader.SplitList(d.Field("domains"));
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

            var parts = UsharpLineReader.SplitParts(line);
            var head = parts[0];
            var fields = UsharpLineReader.ParseFieldBag(parts, 1, StringComparer.OrdinalIgnoreCase);

            if (UsharpLineReader.TryHead(head, "family", out var familyId))
            {
                var family = Family(familyId);
                family.Template = UsharpLineReader.Get(fields, "template") ?? family.Template;
                family.FamilyPrefix = UsharpLineReader.Get(fields, "prefix") ?? family.FamilyPrefix;
                family.Relation = UsharpLineReader.Get(fields, "relation") ?? family.Relation;
                family.Cardinality = UsharpLineReader.Get(fields, "cardinality") ?? family.Cardinality;
                family.BusinessKeyTemplate = UsharpLineReader.Get(fields, "businessKey") ?? family.BusinessKeyTemplate;
                family.DomainId = UsharpLineReader.Get(fields, "domain") ?? family.DomainId;
                family.Aliases.AddRange(UsharpLineReader.SplitList(UsharpLineReader.Get(fields, "aliases")));
                family.OrderKeys.AddRange(UsharpLineReader.SplitList(UsharpLineReader.Get(fields, "orderKeys")));
                family.IdentityKeys.AddRange(UsharpLineReader.SplitList(UsharpLineReader.Get(fields, "identity")));
                family.DerivedPrefixes.AddRange(UsharpLineReader.SplitList(UsharpLineReader.Get(fields, "derivedPrefixes")));
                continue;
            }

            if (UsharpLineReader.TryHead(head, "surface", out var surfaceRef))
            {
                var (family, name) = UsharpLineReader.SplitRef(surfaceRef);
                if (family.Length == 0 || name.Length == 0) { unparsed.Add(line); continue; }
                Family(family).Surfaces.Add(new UsharpKeySurfaceDecl(
                    family, name, UsharpLineReader.SplitList(UsharpLineReader.Get(fields, "templates")),
                    UsharpLineReader.Get(fields, "prefix"), UsharpLineReader.SplitList(UsharpLineReader.Get(fields, "readOnly"))));
                continue;
            }

            if (UsharpLineReader.TryHead(head, "axis", out var axisRef))
            {
                var (family, name) = UsharpLineReader.SplitRef(axisRef);
                if (family.Length == 0 || name.Length == 0) { unparsed.Add(line); continue; }
                Family(family).Axes.Add(new UsharpKeyAxisDecl(
                    family, name, UsharpLineReader.Get(fields, "shape") ?? "text",
                    !string.Equals(UsharpLineReader.Get(fields, "required"), "false", StringComparison.OrdinalIgnoreCase),
                    UsharpLineReader.SplitList(UsharpLineReader.Get(fields, "allowed")), UsharpLineReader.SplitList(UsharpLineReader.Get(fields, "disallow"))));
                continue;
            }

            if (UsharpLineReader.TryHead(head, "alias", out var aliasRef))
            {
                var (family, _) = UsharpLineReader.SplitRef(aliasRef);
                var template = UsharpLineReader.Get(fields, "template") ?? UsharpLineReader.Get(fields, "templates");
                if (family.Length == 0 || string.IsNullOrWhiteSpace(template)) { unparsed.Add(line); continue; }
                Family(family).Aliases.AddRange(UsharpLineReader.SplitList(template));
                continue;
            }

            if (UsharpLineReader.TryHead(head, "derived", out var derivedRef))
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

            if (UsharpLineReader.TryHead(head, "port", out var portName))
            {
                ports.Add(new UsharpKeyPortDecl(
                    portName, UsharpLineReader.Get(fields, "direction") ?? "In", UsharpLineReader.Get(fields, "family") ?? string.Empty,
                    !string.Equals(UsharpLineReader.Get(fields, "required"), "false", StringComparison.OrdinalIgnoreCase),
                    UsharpLineReader.Get(fields, "default"), UsharpLineReader.Get(fields, "cardinality")));
                continue;
            }

            if (UsharpLineReader.TryHead(head, "policy", out var policyKey))
            {
                var value = UsharpLineReader.Get(fields, "value");
                if (string.IsNullOrWhiteSpace(value)) { unparsed.Add(line); continue; }
                policies.Add(new UsharpKeyPolicyDecl(policyKey, value));
                continue;
            }

            if (UsharpLineReader.TryHead(head, "derive", out var deriveName))
            {
                var chain = UsharpLineReader.SplitList(UsharpLineReader.Get(fields, "chain"));
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
}
