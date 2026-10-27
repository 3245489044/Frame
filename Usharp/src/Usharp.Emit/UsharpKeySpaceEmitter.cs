using System.Text.Json.Nodes;
using Usharp.Semantics;
using Usharp.Syntax;

namespace Usharp.Emit;

/// <summary><c>[KeySpace]</c> → 键空间声明 JSON（schema 与 Agent 侧 keyspace loader 同形）。</summary>
public static class UsharpKeySpaceEmitter
{
    /// <summary>发射全部 <c>[KeySpace]</c>（多声明时取首个；缺失 ⇒ 空对象）。</summary>
    public static string Emit(UsharpCompilation compilation)
    {
        ArgumentNullException.ThrowIfNull(compilation);
        var space = compilation.KeySpaces.FirstOrDefault();
        if (space is null) return "{\"id\":\"(empty)\",\"domains\":[],\"families\":[],\"ports\":[],\"policies\":{}}";

        var root = new JsonObject
        {
            ["id"] = space.Id,
            ["domains"] = Array(space.Domains),
        };

        var families = new JsonArray();
        foreach (var family in space.Families)
        {
            var item = new JsonObject
            {
                ["id"] = family.Id,
                ["template"] = family.Template,
            };
            if (family.FamilyPrefix is { } prefix) item["familyPrefix"] = prefix;
            if (family.Relation is { } relation) item["relation"] = Tail(relation);
            if (family.Cardinality is { } cardinality) item["cardinality"] = Tail(cardinality);
            if (family.BusinessKeyTemplate is { } business) item["businessKeyTemplate"] = business;
            if (family.DomainId is { } domain) item["domainId"] = domain;
            if (family.Aliases.Count > 0) item["aliases"] = Array(family.Aliases);
            if (family.OrderKeys.Count > 0) item["orderKeys"] = Array(family.OrderKeys);
            if (family.IdentityKeys.Count > 0) item["identityKeys"] = Array(family.IdentityKeys);
            if (family.DerivedPrefixes.Count > 0) item["derivedPrefixes"] = Array(family.DerivedPrefixes);

            if (family.DerivedPrefixRoles.Count > 0)
            {
                var roles = new JsonObject();
                foreach (var pair in family.DerivedPrefixRoles) roles[pair.Key] = pair.Value;
                item["derivedPrefixRoles"] = roles;
            }

            if (family.Surfaces.Count > 0)
            {
                var surfaces = new JsonObject();
                foreach (var surface in family.Surfaces)
                {
                    var entry = new JsonObject { ["templates"] = Array(surface.Templates) };
                    if (surface.FamilyPrefix is { } surfacePrefix) entry["familyPrefix"] = surfacePrefix;
                    if (surface.ReadOnlyTemplates.Count > 0) entry["readOnlyTemplates"] = Array(surface.ReadOnlyTemplates);
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
                    if (axis.AllowedValues.Count > 0) entry["allowedValues"] = Array(axis.AllowedValues);
                    if (axis.DisallowInfix.Count > 0) entry["disallowInfix"] = Array(axis.DisallowInfix);
                    axes.Add(entry);
                }
                item["axes"] = axes;
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
                ["direction"] = Tail(port.Direction),
                ["familyId"] = port.FamilyId,
                ["required"] = port.Required,
                ["defaultValue"] = port.DefaultValue,
                ["cardinality"] = port.Cardinality is null ? null : Tail(port.Cardinality),
            });
        }
        root["ports"] = ports;

        var policies = new JsonObject();
        foreach (var policy in space.Policies) policies[policy.Key] = policy.Value;
        root["policies"] = policies;

        return root.ToJsonString();
    }

    private static JsonArray Array(IReadOnlyList<string> values)
    {
        var array = new JsonArray();
        foreach (var value in values) array.Add(value);
        return array;
    }

    private static string Tail(string value)
    {
        var dot = value.LastIndexOf('.');
        return dot >= 0 && dot < value.Length - 1 ? value[(dot + 1)..] : value;
    }
}
