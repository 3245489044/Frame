#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using System.Text;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// 表面解析器（**纯函数**）：程序 + 视图 + 档 + 载体 + token 表 → <see cref="ResolvedSurface"/>。
/// <para>
/// 结构解析（映射）与外观解析（材质/层/能力）分离：结构不随档变，外观只由档 + 载体能力决定；
/// 容器与部件共用 <see cref="ResolveMaterial"/>，故气泡与面板是同一套材质系统。
/// </para>
/// <para>一切缺失/不支持进 Issues 并影响指纹（禁静默回退到某个默认载体）。</para>
/// </summary>
public static class SurfaceResolver
{
    /// <summary>缺省外观档 id（声明面 <c>surface_profile_default</c>）。</summary>
    public const string DefaultProfile = "default";

    /// <summary>解析一次视图 × 档 × 载体。</summary>
    /// <param name="program">表面程序（未装配 ⇒ 返回空容器 + program-unavailable）。</param>
    /// <param name="view">视图键。</param>
    /// <param name="carrierId">载体 id（html/winforms/text…）。</param>
    /// <param name="profileId">外观档（空 ⇒ <see cref="DefaultProfile"/>）。</param>
    /// <param name="tokens">token 值表（来自 <see cref="UiDeclaration"/> 解析结果；键名 → 值）。</param>
    /// <param name="overrides">显式覆盖（最高优先级；并入指纹）。</param>
    public static ResolvedSurface Resolve(
        SurfaceProgramDeclaration program,
        string view,
        string carrierId,
        string? profileId = null,
        IReadOnlyDictionary<string, string>? tokens = null,
        IReadOnlyDictionary<string, string>? overrides = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        var carrier = carrierId ?? string.Empty;
        var profileName = string.IsNullOrWhiteSpace(profileId) ? DefaultProfile : profileId!;
        var values = tokens ?? new Dictionary<string, string>(StringComparer.Ordinal);
        var issues = new List<SurfaceIssue>();

        if (!program.IsDeclared)
        {
            issues.Add(new SurfaceIssue("program-unavailable", "surface.generated.json 未装配"));
            return new ResolvedSurface(view ?? string.Empty, profileName, carrier, Fingerprint(program, view, profileName, carrier, overrides), Array.Empty<ResolvedContainer>(), issues);
        }

        var profile = program.EffectiveProfile(profileName);
        if (profile is null)
        {
            issues.Add(new SurfaceIssue("profile-unknown", profileName));
            profile = program.EffectiveProfile(DefaultProfile);
        }

        var carrierSpec = program.Carriers.TryGetValue(carrier, out var spec) ? spec : null;
        if (carrierSpec is null) issues.Add(new SurfaceIssue("carrier-undeclared", carrier));

        var containers = new List<ResolvedContainer>();
        if (!program.Views.TryGetValue(view ?? string.Empty, out var keys))
        {
            issues.Add(new SurfaceIssue("view-undeclared", view ?? string.Empty));
            return new ResolvedSurface(view ?? string.Empty, profileName, carrier, Fingerprint(program, view, profileName, carrier, overrides), containers, issues);
        }

        foreach (var key in keys)
        {
            var container = program.Container(view!, key);
            if (container is null)
            {
                issues.Add(new SurfaceIssue("container-undeclared", view + ":" + key));
                continue;
            }

            containers.Add(ResolveContainer(program, container, profile, carrier, carrierSpec, values));
        }

        return new ResolvedSurface(view!, profileName, carrier, Fingerprint(program, view, profileName, carrier, overrides), containers, issues);
    }

    /// <summary>解析指纹（program + 视图 + 档 + 载体 + 覆盖；确定性）。</summary>
    private static string Fingerprint(
        SurfaceProgramDeclaration program, string? view, string profile, string carrier,
        IReadOnlyDictionary<string, string>? overrides)
    {
        var builder = new StringBuilder();
        builder.Append(program.Fingerprint).Append('|').Append(view ?? string.Empty).Append('|')
            .Append(profile).Append('|').Append(carrier);
        if (overrides is { Count: > 0 })
        {
            foreach (var pair in overrides.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                builder.Append('|').Append(pair.Key).Append('=').Append(pair.Value);
            }
        }

        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(builder.ToString()))).ToLowerInvariant();
    }

    private static ResolvedContainer ResolveContainer(
        SurfaceProgramDeclaration program,
        SurfaceContainerSpec container,
        SurfaceProfileSpec? profile,
        string carrierId,
        SurfaceCarrierSpec? carrierSpec,
        IReadOnlyDictionary<string, string> tokens)
    {
        var issues = new List<SurfaceIssue>();

        IReadOnlyDictionary<string, string> structure = new Dictionary<string, string>(StringComparer.Ordinal);
        if (program.Mappings.TryGetValue(container.Mapping, out var mapping))
        {
            var props = mapping.Carrier(carrierId);
            if (props is null) issues.Add(new SurfaceIssue("mapping-missing", container.Mapping + "@" + carrierId));
            else structure = props;
        }
        else
        {
            issues.Add(new SurfaceIssue("mapping-undeclared", container.Mapping));
        }

        // 槽来源：容器角色派生槽 → 容器显式 appearance 覆盖（容器级优先于档）。
        var slots = new Dictionary<string, string>(StringComparer.Ordinal);
        if (profile is not null)
        {
            foreach (var slot in DerivedSlots(container))
            {
                if (profile.Materials.TryGetValue(slot, out var material)) slots[slot] = material;
            }
        }

        foreach (var pair in container.Appearance) slots[pair.Key] = pair.Value;

        var materials = new Dictionary<string, ResolvedMaterial>(StringComparer.Ordinal);
        foreach (var slot in slots)
        {
            materials[slot.Key] = ResolveMaterial(program, slot.Key, slot.Value, carrierSpec, tokens, issues);
        }

        return new ResolvedContainer(
            container.Id,
            container.Key,
            container.Role,
            container.Slot,
            container.Write,
            container.Parent.Length == 0 ? null : container.Parent,
            structure,
            materials,
            issues);
    }

    /// <summary>容器角色 → 外观槽（与声明面 Profile.materials 的语义槽同名）。</summary>
    private static IEnumerable<string> DerivedSlots(SurfaceContainerSpec container)
    {
        if (container.IsStream) return new[] { "stream_surface" };
        return container.Slot switch
        {
            "aside" => new[] { "sidebar_surface" },
            "composer" => new[] { "composer_surface" },
            "overlay" => new[] { "overlay_surface" },
            "content" => new[] { "panel_surface" },
            _ => Array.Empty<string>(),
        };
    }

    /// <summary>解析一个槽的材质：层栈 + 能力三态 + fallback（做不到 ⇒ 回退到可承载材质）。</summary>
    private static ResolvedMaterial ResolveMaterial(
        SurfaceProgramDeclaration program,
        string slot,
        string materialId,
        SurfaceCarrierSpec? carrierSpec,
        IReadOnlyDictionary<string, string> tokens,
        List<SurfaceIssue> containerIssues)
    {
        if (materialId.Length == 0)
        {
            return new ResolvedMaterial(slot, string.Empty, null, Array.Empty<ResolvedLayer>(), Array.Empty<SurfaceIssue>());
        }

        if (!program.Materials.TryGetValue(materialId, out var material))
        {
            var missing = new SurfaceIssue("material-unknown", slot + ":" + materialId);
            containerIssues.Add(missing);
            return new ResolvedMaterial(slot, materialId, null, Array.Empty<ResolvedLayer>(), new[] { missing });
        }

        var direct = BuildLayers(program, material, carrierSpec, tokens);
        if (!direct.Degraded || material.Fallback.Length == 0
            || !program.Materials.TryGetValue(material.Fallback, out var fallback))
        {
            foreach (var issue in direct.Issues) containerIssues.Add(issue);
            return new ResolvedMaterial(slot, materialId, null, direct.Layers, direct.Issues);
        }

        var degradedIssues = new List<SurfaceIssue>(direct.Issues)
        {
            new("material-degraded", slot + ":" + materialId + "→" + fallback.Name),
        };
        var fallbackLayers = BuildLayers(program, fallback, carrierSpec, tokens);
        degradedIssues.AddRange(fallbackLayers.Issues);
        foreach (var issue in degradedIssues) containerIssues.Add(issue);

        return new ResolvedMaterial(slot, materialId, fallback.Name, fallbackLayers.Layers, degradedIssues);
    }

    private static (IReadOnlyList<ResolvedLayer> Layers, IReadOnlyList<SurfaceIssue> Issues, bool Degraded) BuildLayers(
        SurfaceProgramDeclaration program,
        SurfaceMaterialSpec material,
        SurfaceCarrierSpec? carrierSpec,
        IReadOnlyDictionary<string, string> tokens)
    {
        var layers = new List<ResolvedLayer>();
        var issues = new List<SurfaceIssue>();
        var degraded = false;

        foreach (var reference in material.Layers
                     .OrderBy(pair => LayerZ(program, pair.Value))
                     .ThenBy(pair => pair.Value, StringComparer.Ordinal))
        {
            if (!program.Layers.TryGetValue(reference.Value, out var layer))
            {
                issues.Add(new SurfaceIssue("layer-unknown", material.Name + ":" + reference.Value));
                degraded = true;
                continue;
            }

            var capability = carrierSpec?.Capability(layer.Primitive) ?? "unsupported";
            if (carrierSpec is null) issues.Add(new SurfaceIssue("carrier-undeclared", layer.Primitive));

            if (capability == "unsupported")
            {
                issues.Add(new SurfaceIssue("unsupported", material.Name + ":" + layer.Name + ":" + layer.Primitive));
                degraded = true;
                continue;
            }

            if (capability == "emulated")
            {
                issues.Add(new SurfaceIssue("emulated", material.Name + ":" + layer.Name + ":" + layer.Primitive));
            }

            var parameters = new Dictionary<string, string>(layer.Params.Count, StringComparer.Ordinal);
            foreach (var pair in layer.Params)
            {
                parameters[pair.Key] = tokens.TryGetValue(pair.Value, out var value) ? value : pair.Value;
            }

            layers.Add(new ResolvedLayer(reference.Key, layer.Primitive, layer.Z, parameters));
        }

        return (layers, issues, degraded);
    }

    private static int LayerZ(SurfaceProgramDeclaration program, string layerName)
        => program.Layers.TryGetValue(layerName, out var layer) ? layer.Z : int.MaxValue;
}
