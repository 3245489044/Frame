using Plate.DslMethod.Model;

namespace Usharp.Syntax;

/// <summary>键面声明：<c>surface &lt;family&gt;/&lt;surface&gt; | templates=a,b | prefix=x | readOnly=c</c>。</summary>
public sealed record UsharpKeySurfaceDecl(
    string FamilyId, string Surface, IReadOnlyList<string> Templates,
    string? FamilyPrefix, IReadOnlyList<string> ReadOnlyTemplates);

/// <summary>键轴声明：<c>axis &lt;family&gt;/&lt;name&gt; | shape=text | required=true | canonical=&lt;rule&gt;</c>。</summary>
public sealed record UsharpKeyAxisDecl(
    string FamilyId, string Name, string Shape, bool Required,
    IReadOnlyList<string> AllowedValues, IReadOnlyList<string> DisallowInfix,
    string? Canonical = null);

/// <summary>坐标规范规则：<c>from=&lt;前缀|*&gt; | rule=strip-prefix|identity | on-miss=Fail|Keep</c>。</summary>
public sealed record UsharpKeyCanonicalRuleDecl(
    string From, string Rule, string OnMiss, string? Family);

/// <summary>坐标规范规则集：<c>canonical &lt;family&gt;/&lt;axis&gt;/&lt;name&gt; | idempotent=True</c>。</summary>
public sealed record UsharpKeyCanonicalSetDecl(
    string FamilyId, string Axis, string Name, bool Idempotent,
    IReadOnlyList<UsharpKeyCanonicalRuleDecl> Rules);

/// <summary>键族声明：身份模板 + 面 + 轴 + 别名 + 派生前缀（+ 可逆性与坐标规则集）。</summary>
public sealed record UsharpKeyFamilyDecl(
    string Id, string Template, string? FamilyPrefix, string? Relation, string? Cardinality,
    string? BusinessKeyTemplate, IReadOnlyList<string> Aliases, IReadOnlyList<string> OrderKeys,
    IReadOnlyList<string> IdentityKeys, string? DomainId, IReadOnlyList<string> DerivedPrefixes,
    IReadOnlyDictionary<string, string> DerivedPrefixRoles,
    IReadOnlyList<UsharpKeySurfaceDecl> Surfaces, IReadOnlyList<UsharpKeyAxisDecl> Axes,
    IReadOnlyDictionary<string, bool> DerivedPrefixReversibility,
    IReadOnlyList<UsharpKeyCanonicalSetDecl> CanonicalSets,
    bool? Authority = null, string? KindLexicon = null);

/// <summary>键端口声明：<c>port &lt;name&gt; | direction=In | family=&lt;id&gt; | required=true</c>。</summary>
public sealed record UsharpKeyPortDecl(
    string Name, string Direction, string FamilyId, bool Required,
    string? DefaultValue, string? Cardinality);

/// <summary>空集策略声明：<c>policy &lt;key&gt; | value=CreateMember</c>。</summary>
public sealed record UsharpKeyPolicyDecl(string Key, string Value);

/// <summary>必备策略声明：<c>required-policy &lt;key&gt;</c>（缺项即装配期红，防声明漂移）。</summary>
public sealed record UsharpKeyRequiredPolicyDecl(string Key);

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
    string SourceFile, int LineNumber,
    IReadOnlyList<UsharpKeyRequiredPolicyDecl>? RequiredPolicies = null)
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
        var requiredPolicies = new List<UsharpKeyRequiredPolicyDecl>();
        var derives = new List<UsharpKeyDeriveDecl>();
        var unparsed = new List<string>();
        CanonicalBuilder? canonical = null;

        FamilyBuilder Family(string id)
        {
            if (byFamily.TryGetValue(id, out var existing)) return existing;
            var created = new FamilyBuilder(id);
            byFamily[id] = created;
            families.Add(created);
            return created;
        }

        void FlushCanonical()
        {
            if (canonical is null) return;
            Family(canonical.FamilyId).CanonicalSets.Add(new UsharpKeyCanonicalSetDecl(
                canonical.FamilyId, canonical.Axis, canonical.Name, canonical.Idempotent,
                canonical.Rules.ToList()));
            canonical = null;
        }

        foreach (var raw in d.Continuation)
        {
            var line = (raw ?? string.Empty).Trim();
            if (line.Length == 0) continue;

            var parts = UsharpLineReader.SplitParts(line);
            var head = parts[0];
            var fields = UsharpLineReader.ParseFieldBag(parts, 1, StringComparer.OrdinalIgnoreCase);

            // canonical 段内的规则续行（from=…）；非规则行 ⇒ 结束该段回到常规首词解析。
            if (canonical is not null)
            {
                var ruleFields = UsharpLineReader.ParseFieldBag(parts, 0, StringComparer.OrdinalIgnoreCase);
                if (ruleFields.TryGetValue("from", out var ruleFrom) && ruleFrom.Length > 0)
                {
                    var rule = UsharpLineReader.Get(ruleFields, "rule");
                    if (string.IsNullOrWhiteSpace(rule)) { unparsed.Add(line); continue; }
                    canonical.Rules.Add(new UsharpKeyCanonicalRuleDecl(
                        ruleFrom, rule, UsharpLineReader.Get(ruleFields, "on-miss") ?? "Fail",
                        UsharpLineReader.Get(ruleFields, "family")));
                    continue;
                }

                FlushCanonical();
            }

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
                family.Authority = ParseOptionalBool(UsharpLineReader.Get(fields, "authority")) ?? family.Authority;
                family.KindLexicon = UsharpLineReader.Get(fields, "kind-lexicon") ?? family.KindLexicon;
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
                    UsharpLineReader.SplitList(UsharpLineReader.Get(fields, "allowed")), UsharpLineReader.SplitList(UsharpLineReader.Get(fields, "disallow")),
                    UsharpLineReader.Get(fields, "canonical")));
                continue;
            }

            if (UsharpLineReader.TryHead(head, "canonical", out var canonicalRef))
            {
                var segments = canonicalRef.Split('/', StringSplitOptions.TrimEntries);
                if (segments.Length != 3 || segments.Any(static s => s.Length == 0))
                {
                    unparsed.Add(line);
                    continue;
                }

                canonical = new CanonicalBuilder(
                    segments[0], segments[1], segments[2],
                    !string.Equals(UsharpLineReader.Get(fields, "idempotent"), "false", StringComparison.OrdinalIgnoreCase));
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
                var bag = UsharpFieldBag.Parse(new[] { derivedRef }, skip: 0, comparer: StringComparer.Ordinal);
                if (bag.Fields.Count == 0) { unparsed.Add(line); continue; }

                var derivedPair = bag.Fields.First();
                var familyRole = derivedPair.Key;
                var prefix = derivedPair.Value;
                var slash = familyRole.IndexOf('/');
                if (slash <= 0 || slash >= familyRole.Length - 1 || prefix.Length == 0) { unparsed.Add(line); continue; }
                var builder = Family(familyRole[..slash]);
                var reversible = !string.Equals(
                    UsharpLineReader.Get(fields, "reversible"), "false", StringComparison.OrdinalIgnoreCase);
                if (!builder.DerivedPrefixes.Contains(prefix, StringComparer.Ordinal))
                {
                    builder.DerivedPrefixes.Add(prefix);
                }
                builder.DerivedPrefixReversibility[prefix] = reversible;
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

            if (UsharpLineReader.TryHead(head, "required-policy", out var requiredPolicyKey))
            {
                if (string.IsNullOrWhiteSpace(requiredPolicyKey)) { unparsed.Add(line); continue; }
                requiredPolicies.Add(new UsharpKeyRequiredPolicyDecl(requiredPolicyKey.Trim()));
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

        FlushCanonical();

        return new UsharpKeySpaceDecl(
            d.Name.Trim(), domains, families.Select(f => f.Build()).ToList(),
            ports, policies, derives, unparsed, d.SourceFile, d.LineNumber,
            requiredPolicies.Count > 0 ? requiredPolicies : null);
    }

    private static bool? ParseOptionalBool(string? value)
        => string.IsNullOrWhiteSpace(value)
            ? null
            : string.Equals(value, "true", StringComparison.OrdinalIgnoreCase);

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
        public Dictionary<string, bool> DerivedPrefixReversibility { get; } = new(StringComparer.Ordinal);
        public bool? Authority { get; set; }
        public string? KindLexicon { get; set; }
        public List<UsharpKeySurfaceDecl> Surfaces { get; } = new();
        public List<UsharpKeyAxisDecl> Axes { get; } = new();
        public List<UsharpKeyCanonicalSetDecl> CanonicalSets { get; } = new();

        public UsharpKeyFamilyDecl Build() => new(
            Id, Template, FamilyPrefix, Relation, Cardinality, BusinessKeyTemplate,
            Aliases, OrderKeys, IdentityKeys, DomainId, DerivedPrefixes, DerivedPrefixRoles, Surfaces, Axes,
            DerivedPrefixReversibility, CanonicalSets, Authority, KindLexicon);
    }

    private sealed class CanonicalBuilder
    {
        public CanonicalBuilder(string familyId, string axis, string name, bool idempotent)
        {
            FamilyId = familyId;
            Axis = axis;
            Name = name;
            Idempotent = idempotent;
        }

        public string FamilyId { get; }
        public string Axis { get; }
        public string Name { get; }
        public bool Idempotent { get; }
        public List<UsharpKeyCanonicalRuleDecl> Rules { get; } = new();
    }
}
