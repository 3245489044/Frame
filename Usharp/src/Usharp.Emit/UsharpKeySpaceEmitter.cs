using System.Text.Json.Nodes;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Emit;

/// <summary>
/// 键空间发射产物：单载荷 JSON（不可产出 ⇒ <c>null</c>）+ 发射期诊断。
/// <para>取代「多声明取首个 / 零声明 <c>id="(empty)"</c> 伪对象」的静默行为。</para>
/// </summary>
public sealed record UsharpKeySpaceResult(
    string? Json,
    IReadOnlyList<UsharpDiagnostic> Diagnostics) : UsharpEmitResultBase(Diagnostics);

/// <summary><c>[KeySpace]</c> → 键空间声明 JSON（schema 与 Agent 侧 keyspace loader 同形）。</summary>
public static class UsharpKeySpaceEmitter
{
    /// <summary>发射本编译的 <c>[KeySpace]</c>（多声明 / 无声明 ⇒ 不产出 + 诊断可见化）。</summary>
    /// <param name="compilation">编译结果。</param>
    /// <param name="sourceFingerprint">源声明内容指纹（SHA256 小写 hex；构建期计算，运行期自证新鲜度）。</param>
    /// <param name="generatorVersion">生成器版本（产物↔生成器对的显式标记）。</param>
    public static UsharpKeySpaceResult Emit(
        UsharpCompilation compilation, string? sourceFingerprint = null, string? generatorVersion = null)
    {
        ArgumentNullException.ThrowIfNull(compilation);

        var diagnostics = new List<UsharpDiagnostic>();
        if (compilation.KeySpaces.Count == 0)
        {
            diagnostics.Add(UsharpDiagnostic.Info(UsharpCodes.SingleFileNoKeySpace,
                "[KeySpace] 未声明（本编译无可发射键空间）",
                compilation.Documents.Count > 0 ? compilation.Documents[0].FilePath : ".",
                1));
            return new UsharpKeySpaceResult(null, diagnostics);
        }

        if (compilation.KeySpaces.Count > 1)
        {
            var extra = compilation.KeySpaces[1];
            diagnostics.Add(UsharpDiagnostic.Error(UsharpCodes.KeySpaceDuplicate,
                $"[KeySpace] 多声明（{string.Join(", ", compilation.KeySpaces.Select(s => s.Id))}）："
                + "一次发射只接受一个键空间（禁静默取首个）",
                extra.SourceFile, extra.LineNumber));
            return new UsharpKeySpaceResult(null, diagnostics);
        }

        var space = compilation.KeySpaces[0];

        var root = new JsonObject
        {
            ["id"] = space.Id,
            ["domains"] = UsharpJson.Array(space.Domains),
        };
        AddProvenance(root, sourceFingerprint, generatorVersion);

        var families = new JsonArray();
        foreach (var family in space.Families)
        {
            var item = new JsonObject
            {
                ["id"] = family.Id,
                ["template"] = family.Template,
            };
            if (family.FamilyPrefix is { } prefix) item["familyPrefix"] = prefix;
            if (family.Relation is { } relation) item["relation"] = UsharpJson.Tail(relation);
            if (family.Cardinality is { } cardinality) item["cardinality"] = UsharpJson.Tail(cardinality);
            if (family.BusinessKeyTemplate is { } business) item["businessKeyTemplate"] = business;
            if (family.DomainId is { } domain) item["domainId"] = domain;
            if (family.Aliases.Count > 0) item["aliases"] = UsharpJson.Array(family.Aliases);
            if (family.OrderKeys.Count > 0) item["orderKeys"] = UsharpJson.Array(family.OrderKeys);
            if (family.IdentityKeys.Count > 0) item["identityKeys"] = UsharpJson.Array(family.IdentityKeys);
            if (family.DerivedPrefixes.Count > 0) item["derivedPrefixes"] = UsharpJson.Array(family.DerivedPrefixes);

            if (family.DerivedPrefixRoles.Count > 0)
            {
                var roles = new JsonObject();
                foreach (var pair in family.DerivedPrefixRoles) roles[pair.Key] = pair.Value;
                item["derivedPrefixRoles"] = roles;
            }

            if (family.DerivedPrefixReversibility.Count > 0)
            {
                var reversibility = new JsonObject();
                foreach (var pair in family.DerivedPrefixReversibility) reversibility[pair.Key] = pair.Value;
                item["derivedPrefixReversibility"] = reversibility;
            }

            if (family.Authority is { } authority) item["authority"] = authority;
            if (family.KindLexicon is { } kindLexicon) item["kindLexicon"] = kindLexicon;

            if (family.Surfaces.Count > 0)
            {
                var surfaces = new JsonObject();
                foreach (var surface in family.Surfaces)
                {
                    var entry = new JsonObject { ["templates"] = UsharpJson.Array(surface.Templates) };
                    if (surface.FamilyPrefix is { } surfacePrefix) entry["familyPrefix"] = surfacePrefix;
                    if (surface.ReadOnlyTemplates.Count > 0) entry["readOnlyTemplates"] = UsharpJson.Array(surface.ReadOnlyTemplates);
                    surfaces[surface.Surface] = entry;
                }
                item["surfaces"] = surfaces;
            }

            if (family.Axes.Count > 0)
            {
                var axes = new JsonArray();
                foreach (var axis in family.Axes)
                {
                    var entry = new JsonObject
                    {
                        ["name"] = axis.Name,
                        ["shape"] = axis.Shape,
                        ["required"] = axis.Required,
                    };
                    if (axis.AllowedValues.Count > 0) entry["allowedValues"] = UsharpJson.Array(axis.AllowedValues);
                    if (axis.DisallowInfix.Count > 0) entry["disallowInfix"] = UsharpJson.Array(axis.DisallowInfix);
                    if (axis.Canonical is { } axisCanonical) entry["canonical"] = axisCanonical;
                    axes.Add(entry);
                }
                item["axes"] = axes;
            }

            if (family.CanonicalSets.Count > 0)
            {
                var canonical = new JsonObject();
                foreach (var set in family.CanonicalSets)
                {
                    var rules = new JsonArray();
                    foreach (var rule in set.Rules)
                    {
                        var entry = new JsonObject
                        {
                            ["from"] = rule.From,
                            ["rule"] = rule.Rule,
                            ["onMiss"] = rule.OnMiss,
                        };
                        if (rule.Family is { } ruleFamily) entry["family"] = ruleFamily;
                        rules.Add(entry);
                    }

                    canonical[set.Name] = new JsonObject
                    {
                        ["family"] = set.FamilyId,
                        ["axis"] = set.Axis,
                        ["idempotent"] = set.Idempotent,
                        ["rules"] = rules,
                    };
                }
                item["canonical"] = canonical;
            }

            families.Add(item);
        }
        root["families"] = families;

        var ports = new JsonArray();
        foreach (var port in space.Ports)
        {
            ports.Add(new JsonObject
            {
                ["name"] = port.Name,
                ["direction"] = UsharpJson.Tail(port.Direction),
                ["familyId"] = port.FamilyId,
                ["required"] = port.Required,
                ["defaultValue"] = port.DefaultValue,
                ["cardinality"] = port.Cardinality is null ? null : UsharpJson.Tail(port.Cardinality),
            });
        }
        root["ports"] = ports;

        var policies = new JsonObject();
        foreach (var policy in space.Policies) policies[policy.Key] = policy.Value;
        root["policies"] = policies;

        var required = space.RequiredPolicies;
        if (required is { Count: > 0 })
        {
            var requiredArray = new JsonArray();
            foreach (var item in required) requiredArray.Add(item.Key);
            root["requiredPolicies"] = requiredArray;
        }

        if (space.Derives.Count > 0)
        {
            var derives = new JsonObject();
            foreach (var derive in space.Derives) derives[derive.Name] = UsharpJson.Array(derive.Chain);
            root["derives"] = derives;
        }

        return new UsharpKeySpaceResult(root.ToJsonString(), diagnostics);
    }

    private static void AddProvenance(JsonObject root, string? sourceFingerprint, string? generatorVersion)
    {
        if (!string.IsNullOrWhiteSpace(sourceFingerprint))
        {
            root["sourceFingerprint"] = sourceFingerprint;
        }

        root["generatorVersion"] = string.IsNullOrWhiteSpace(generatorVersion)
            ? (typeof(UsharpKeySpaceEmitter).Assembly.GetName().Version?.ToString() ?? "0.0.0")
            : generatorVersion;
    }
}
