#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using Share.Conversation.Rendering.Surface.Selection;
using UiBackend;

namespace Share.Conversation.Rendering.Surface.Gather;

/// <summary>
/// 表面计划装配（**唯一入口**）：把声明 + 档 + 用户覆盖组装成只读生效快照
/// <see cref="SurfacePlanBundle"/>，两载体（HTML / WinForms）与缓存键都只读快照。
/// <para>纪律：本类不读环境/存储；覆盖项由选择源显式传入。</para>
/// </summary>
public static class SurfacePlanBuilder
{
    /// <summary>覆盖令牌的来源标记。</summary>
    public const string SourceOverride = "override";

    /// <summary>声明令牌的来源标记。</summary>
    public const string SourceDeclared = "declared";

    /// <summary>
    /// 组装当前载体的全视图计划。
    /// </summary>
    /// <param name="program">表面程序。</param>
    /// <param name="carrier">载体 id。</param>
    /// <param name="selection">选择快照（含档与覆盖）。</param>
    /// <param name="propertyChannel">属性写面通道（可空 ⇒ 不解析属性视图）。</param>
    /// <param name="profileOverride">强制档（装配期用将要生效的档，而不是选择源里的旧档）。</param>
    public static SurfacePlanBundle Build(
        SurfaceProgram program,
        string carrier,
        SurfaceSelection selection,
        string? propertyChannel = null,
        string? profileOverride = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(selection);
        if (!program.IsDeclared)
        {
            return new SurfacePlanBundle(carrier ?? string.Empty, string.Empty, EmptyViews(), Array.Empty<SurfaceIssue>());
        }

        var effectiveCarrier = carrier ?? string.Empty;
        var profile = string.IsNullOrWhiteSpace(profileOverride) ? selection.ProfileId : profileOverride!;
        var declared = SurfaceResolver.TokensForProfile(program.Declaration, profile);
        var effective = ApplyOverrides(declared, selection.Overrides, out var sources);

        var views = new Dictionary<string, ResolvedSurface>(StringComparer.Ordinal);
        var issues = new List<SurfaceIssue>();
        foreach (var view in program.Views.Keys)
        {
            var resolved = SurfaceResolver.Resolve(
                program.Declaration,
                view,
                effectiveCarrier,
                profile,
                tokens: effective,
                overrides: selection.Overrides);
            views[view] = resolved;
            CollectIssues(view, resolved, issues);
        }

        var propertyViews = new Dictionary<string, ResolvedSurface>(StringComparer.Ordinal);
        if (!string.IsNullOrWhiteSpace(propertyChannel))
        {
            foreach (var view in program.Views.Keys)
            {
                propertyViews[view] = SurfaceResolver.Resolve(
                    program.Declaration,
                    view,
                    effectiveCarrier,
                    profile,
                    tokens: effective,
                    overrides: selection.Overrides,
                    channel: propertyChannel);
            }
        }

        return new SurfacePlanBundle(effectiveCarrier, profile, views, Distinguish(issues))
        {
            Tokens = effective,
            DeclaredTokens = declared,
            TokenSources = sources,
            Settings = program.Declaration.Settings,
            PropertyViews = propertyViews,
            GlassLook = GlassLookOf(program.Declaration),
            EngineLight = EngineLightOf(effective),
            RoleSlots = RoleSlotsOf(program.Declaration),
        };
    }

    /// <summary>
    /// 叠加用户覆盖（最高优先级）并记录来源。
    /// </summary>
    /// <param name="declared">声明令牌。</param>
    /// <param name="overrides">覆盖令牌（可空）。</param>
    /// <param name="sources">键 → 来源（仅覆盖键）。</param>
    public static Dictionary<string, string> ApplyOverrides(
        IReadOnlyDictionary<string, string> declared,
        IReadOnlyDictionary<string, string>? overrides,
        out IReadOnlyDictionary<string, string> sources)
    {
        var tokens = new Dictionary<string, string>(declared, StringComparer.Ordinal);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (overrides is not null)
        {
            foreach (var pair in overrides)
            {
                if (string.IsNullOrEmpty(pair.Key)) continue;
                tokens[pair.Key] = pair.Value ?? string.Empty;
                map[pair.Key] = SourceOverride;
            }
        }

        sources = map;
        return tokens;
    }

    /// <summary>玻璃形态声明表（<c>glass_*</c> 的 tier 键 <c>t0..t4</c>；未声明 ⇒ 空表）。</summary>
    /// <param name="declaration">表面声明。</param>
    public static SurfaceGlassLookTable GlassLookOf(SurfaceProgramDeclaration declaration)
    {
        ArgumentNullException.ThrowIfNull(declaration);
        var layers = new Dictionary<GlassTier, SurfaceGlassLayer>();
        foreach (var tier in new[] { GlassTier.T0, GlassTier.T1, GlassTier.T2, GlassTier.T3, GlassTier.T4 })
        {
            var key = "t" + ((int)tier).ToString(CultureInfo.InvariantCulture);
            var hasAny = false;
            var blur = TierFloat(declaration, "glass_blur_sigma", key, ref hasAny);
            var refract = TierFloat(declaration, "glass_refract_strength", key, ref hasAny);
            var dispersion = TierFloat(declaration, "glass_dispersion", key, ref hasAny);
            var fresnel = TierFloat(declaration, "glass_fresnel_strength", key, ref hasAny);
            if (hasAny)
            {
                layers[tier] = new SurfaceGlassLayer(blur, refract, dispersion, fresnel);
            }
        }

        if (layers.Count == 0) return SurfaceGlassLookTable.Empty;

        var tintArgb = 0;
        if (declaration.Tokens.TryGetValue("glass_tint", out var tint)
            && TryDefault(tint, out var text)
            && SurfaceColor.TryParseColor(text, out var argb))
        {
            tintArgb = argb;
        }

        return new SurfaceGlassLookTable(layers, tintArgb);
    }

    /// <summary>引擎光 look 子集（<c>light_*</c>；未声明字段保持 NaN ⇒ 引擎派生）。</summary>
    /// <param name="tokens">生效令牌表。</param>
    public static SurfaceLightLook EngineLightOf(IReadOnlyDictionary<string, string> tokens)
    {
        ArgumentNullException.ThrowIfNull(tokens);
        var tint = 0;
        if (tokens.TryGetValue("light_tint", out var tintText)
            && SurfaceColor.TryParseColor(tintText, out var argb))
        {
            tint = argb;
        }

        return new SurfaceLightLook(
            TokenFloat(tokens, "light_sun_azimuth"),
            TokenFloat(tokens, "light_sun_elevation"),
            TokenFloat(tokens, "light_sun_intensity"),
            TokenFloat(tokens, "light_ambient"),
            TokenFloat(tokens, "light_warmth"),
            tint);
    }

    /// <summary>角色 → 材质槽（声明 <c>role_*_slot</c>）。</summary>
    /// <param name="declaration">表面声明。</param>
    public static IReadOnlyDictionary<string, string> RoleSlotsOf(SurfaceProgramDeclaration declaration)
    {
        var slots = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var role in new[] { "host", "card", "nested", "leaf" })
        {
            if (declaration.Settings.TryGetValue("role_" + role + "_slot", out var slot) && slot.Length > 0)
            {
                slots[role] = slot;
            }
        }

        return slots;
    }

    private static void CollectIssues(string view, ResolvedSurface resolved, List<SurfaceIssue> issues)
    {
        foreach (var issue in resolved.Issues)
        {
            issues.Add(new SurfaceIssue(issue.Code, view + ":" + issue.Detail));
        }

        foreach (var container in resolved.Containers)
        {
            foreach (var issue in container.Issues)
            {
                issues.Add(new SurfaceIssue(issue.Code, view + ":" + container.Id + ":" + issue.Detail));
            }

            foreach (var material in container.Materials.Values)
            {
                foreach (var issue in material.Issues)
                {
                    issues.Add(new SurfaceIssue(
                        issue.Code, view + ":" + container.Id + ":" + material.Slot + ":" + issue.Detail));
                }
            }
        }
    }

    private static float TokenFloat(IReadOnlyDictionary<string, string> tokens, string key)
        => tokens.TryGetValue(key, out var text)
           && float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
           && float.IsFinite(value)
            ? value
            : float.NaN;

    private static float TierFloat(
        SurfaceProgramDeclaration declaration, string token, string tier, ref bool hasAny)
    {
        if (!declaration.Tokens.TryGetValue(token, out var values) || !values.TryGetValue(tier, out var text))
        {
            return 0f;
        }

        if (!float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)
            || !float.IsFinite(value))
        {
            return 0f;
        }

        hasAny = true;
        return value;
    }

    private static bool TryDefault(IReadOnlyDictionary<string, string> values, out string text)
    {
        if (values.TryGetValue("default", out var declaredText) && declaredText.Length > 0)
        {
            text = declaredText;
            return true;
        }

        foreach (var pair in values)
        {
            if (pair.Value.Length > 0)
            {
                text = pair.Value;
                return true;
            }
        }

        text = string.Empty;
        return false;
    }

    private static IReadOnlyDictionary<string, ResolvedSurface> EmptyViews()
        => new Dictionary<string, ResolvedSurface>(StringComparer.Ordinal);

    private static IReadOnlyList<SurfaceIssue> Distinguish(List<SurfaceIssue> issues)
    {
        if (issues.Count == 0) return Array.Empty<SurfaceIssue>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var result = new List<SurfaceIssue>(issues.Count);
        foreach (var issue in issues)
        {
            if (seen.Add(issue.Code + "|" + issue.Detail)) result.Add(issue);
        }

        return result;
    }
}
