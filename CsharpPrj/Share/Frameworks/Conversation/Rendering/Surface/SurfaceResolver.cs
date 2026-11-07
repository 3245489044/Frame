#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Modules.CapabilityResolution;
using UiBackend;

namespace Share.Conversation.Rendering.Surface;

/// <summary>一次解析用到的声明派生输入（能力矩阵 / 层规格 / 资源问题）。</summary>
/// <param name="Capabilities">载体 × 通道 × 图元能力矩阵。</param>
/// <param name="LayerSpecs">层名 → 内核层规格（资源 id 已换 src）。</param>
/// <param name="AssetIssuesByLayer">层名 → 资源问题（未声明 asset）。</param>
public sealed record SurfaceResolveContext(
    CapabilityMatrix Capabilities,
    IReadOnlyDictionary<string, LayerSpec> LayerSpecs,
    IReadOnlyDictionary<string, SurfaceIssue> AssetIssuesByLayer);

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

    /// <summary>控件面槽 id：属性通道唯一允许保留 fill 到 L2 的槽（页面/宿主槽不得走梯度保留）。</summary>
    public const string ControlFaceSlot = "control_face";

    /// <summary>
    /// 解析外观档的单个语义槽材质（**与容器同一个 <see cref="ResolveMaterial"/>**，不写第二套）。
    /// <para>用途：属性通道全局回退（<c>row_surface</c>）等「不挂在容器上的槽」只能从档位取，禁止调用方自建解析。</para>
    /// <para>档/槽缺失 ⇒ null（调用方回落既有选择，零回归）。</para>
    /// </summary>
    /// <param name="program">表面程序。</param>
    /// <param name="context">解析上下文（能力矩阵 / 层规格 / 资源问题）。</param>
    /// <param name="profileId">外观档（空 ⇒ <see cref="DefaultProfile"/>；未知 ⇒ 回落默认档）。</param>
    /// <param name="carrierId">载体 id。</param>
    /// <param name="slot">语义槽名（<c>panel_surface</c> / <c>row_surface</c> …）。</param>
    /// <param name="channel">写面通道（空 ⇒ <see cref="CapabilityMatrix.DefaultChannel"/>）。</param>
    public static ResolvedMaterial? ResolveProfileSlot(
        SurfaceProgramDeclaration program,
        SurfaceResolveContext context,
        string? profileId,
        string carrierId,
        string slot,
        string? channel = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(context);
        if (string.IsNullOrWhiteSpace(slot)) return null;

        var profileName = string.IsNullOrWhiteSpace(profileId) ? DefaultProfile : profileId!;
        var profile = program.EffectiveProfile(profileName) ?? program.EffectiveProfile(DefaultProfile);
        if (profile is null || !profile.Materials.TryGetValue(slot, out var materialId)) return null;

        var issues = new List<SurfaceIssue>();
        var material = ResolveMaterial(
            program,
            carrierId ?? string.Empty,
            slot,
            materialId,
            context.Capabilities,
            context.LayerSpecs,
            TokensForProfile(program, profileName),
            string.IsNullOrWhiteSpace(channel) ? CapabilityMatrix.DefaultChannel : channel!,
            context.AssetIssuesByLayer,
            issues);
        return material.Layers.Count > 0 ? material : null;
    }

    /// <summary>解析一次视图 × 档 × 载体。</summary>
    /// <param name="program">表面程序（未装配 ⇒ 返回空容器 + program-unavailable）。</param>
    /// <param name="view">视图键。</param>
    /// <param name="carrierId">载体 id（html/winforms/text…）。</param>
    /// <param name="profileId">外观档（空 ⇒ <see cref="DefaultProfile"/>）。</param>
    /// <param name="tokens">token 值表（来自 <see cref="UiDeclaration"/> 解析结果；键名 → 值）。</param>
    /// <param name="overrides">显式覆盖（最高优先级；并入指纹）。</param>
    /// <summary>
    /// 兼容重载（六参数，channel=draw）：保护已烘焙调用方的二进制绑定（禁以可选参数替代重载）。
    /// </summary>
    /// <param name="program">表面程序。</param>
    /// <param name="view">视图键。</param>
    /// <param name="carrierId">载体 id。</param>
    /// <param name="profileId">外观档（空 ⇒ 默认档）。</param>
    /// <param name="tokens">token 值表。</param>
    /// <param name="overrides">显式覆盖。</param>
    public static ResolvedSurface Resolve(
        SurfaceProgramDeclaration program,
        string view,
        string carrierId,
        string? profileId,
        IReadOnlyDictionary<string, string>? tokens,
        IReadOnlyDictionary<string, string>? overrides)
        => Resolve(program, view, carrierId, profileId, tokens, overrides, channel: null);

    /// <summary>解析一次视图 × 档 × 载体 × 通道。</summary>
    /// <param name="program">表面程序（未装配 ⇒ 返回空容器 + program-unavailable）。</param>
    /// <param name="view">视图键。</param>
    /// <param name="carrierId">载体 id（html/winforms/text…）。</param>
    /// <param name="profileId">外观档（空 ⇒ <see cref="DefaultProfile"/>）。</param>
    /// <param name="tokens">token 值表（键名 → 值）。</param>
    /// <param name="overrides">显式覆盖（最高优先级；并入指纹）。</param>
    /// <param name="channel">写面通道（空 ⇒ <see cref="CapabilityMatrix.DefaultChannel"/>）。</param>
    public static ResolvedSurface Resolve(
        SurfaceProgramDeclaration program,
        string view,
        string carrierId,
        string? profileId = null,
        IReadOnlyDictionary<string, string>? tokens = null,
        IReadOnlyDictionary<string, string>? overrides = null,
        string? channel = null)
        => Resolve(program, Prepare(program), view, carrierId, profileId, tokens, overrides, channel);

    /// <summary>解析一次视图 × 档 × 载体 × 通道（复用已构建的解析上下文；每视图只建一次矩阵/层规格）。</summary>
    /// <param name="program">表面程序（未装配 ⇒ 返回空容器 + program-unavailable）。</param>
    /// <param name="context">解析上下文（能力矩阵 / 层规格 / 资源问题）。</param>
    /// <param name="view">视图键。</param>
    /// <param name="carrierId">载体 id（html/winforms/text…）。</param>
    /// <param name="profileId">外观档（空 ⇒ <see cref="DefaultProfile"/>）。</param>
    /// <param name="tokens">token 值表（键名 → 值）。</param>
    /// <param name="overrides">显式覆盖（最高优先级；并入指纹）。</param>
    /// <param name="channel">写面通道（空 ⇒ <see cref="CapabilityMatrix.DefaultChannel"/>）。</param>
    public static ResolvedSurface Resolve(
        SurfaceProgramDeclaration program,
        SurfaceResolveContext context,
        string view,
        string carrierId,
        string? profileId = null,
        IReadOnlyDictionary<string, string>? tokens = null,
        IReadOnlyDictionary<string, string>? overrides = null,
        string? channel = null)
    {
        ArgumentNullException.ThrowIfNull(program);
        ArgumentNullException.ThrowIfNull(context);
        var carrier = carrierId ?? string.Empty;
        var profileName = string.IsNullOrWhiteSpace(profileId) ? DefaultProfile : profileId!;
        // 档位令牌表（program.tokens）先铺底，调用方显式 token 覆盖（调用方优先级更高）。
        var values = new Dictionary<string, string>(TokensForProfile(program, profileName), StringComparer.Ordinal);
        if (tokens is not null)
        {
            foreach (var pair in tokens) values[pair.Key] = pair.Value;
        }

        var issues = new List<SurfaceIssue>();
        var resolvedChannel = string.IsNullOrWhiteSpace(channel) ? CapabilityMatrix.DefaultChannel : channel!;
        var fingerprintChannel = string.Equals(resolvedChannel, CapabilityMatrix.DefaultChannel, StringComparison.Ordinal)
            ? null
            : resolvedChannel;

        if (!program.IsDeclared)
        {
            issues.Add(new SurfaceIssue("program-unavailable", "surface.generated.json 未装配"));
            return new ResolvedSurface(view ?? string.Empty, profileName, carrier, Fingerprint(program, view, profileName, carrier, overrides, fingerprintChannel), Array.Empty<ResolvedContainer>(), issues);
        }

        var profile = program.EffectiveProfile(profileName);
        if (profile is null)
        {
            issues.Add(new SurfaceIssue("profile-unknown", profileName));
            profile = program.EffectiveProfile(DefaultProfile);
        }

        if (!program.Carriers.ContainsKey(carrier)) issues.Add(new SurfaceIssue(ResolutionIssueCodes.CarrierUndeclared, carrier));

        var capabilities = context.Capabilities;
        var layerSpecs = context.LayerSpecs;
        var assetIssuesByLayer = context.AssetIssuesByLayer;

        var containers = new List<ResolvedContainer>();
        if (!program.Views.TryGetValue(view ?? string.Empty, out var keys))
        {
            issues.Add(new SurfaceIssue("view-undeclared", view ?? string.Empty));
            return new ResolvedSurface(view ?? string.Empty, profileName, carrier, Fingerprint(program, view, profileName, carrier, overrides, fingerprintChannel), containers, issues);
        }

        foreach (var key in keys)
        {
            var container = program.Container(view!, key);
            if (container is null)
            {
                issues.Add(new SurfaceIssue("container-undeclared", view + ":" + key));
                continue;
            }

            containers.Add(ResolveContainer(program, container, profile, carrier, values, capabilities, layerSpecs, assetIssuesByLayer, resolvedChannel));
        }

        return new ResolvedSurface(view!, profileName, carrier, Fingerprint(program, view, profileName, carrier, overrides, fingerprintChannel), containers, issues);
    }

    /// <summary>解析指纹（program + 视图 + 档 + 载体 + 覆盖 + 非默认通道；确定性）。</summary>
    private static string Fingerprint(
        SurfaceProgramDeclaration program, string? view, string profile, string carrier,
        IReadOnlyDictionary<string, string>? overrides, string? channel = null)
    {
        var builder = new StringBuilder();
        builder.Append(program.Fingerprint).Append('|').Append(view ?? string.Empty).Append('|')
            .Append(profile).Append('|').Append(carrier);
        if (channel is not null) builder.Append('|').Append(channel);
        if (overrides is { Count: > 0 })
        {
            foreach (var pair in overrides.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                builder.Append('|').Append(pair.Key).Append('=').Append(pair.Value);
            }
        }

        return Signature.Sha256(builder.ToString());
    }

    /// <summary>
    /// 取某外观档的令牌值表（token → 值）：档位 = <c>Profile.values.ui_skin</c>，缺该档位回 <c>default</c>。
    /// <para>这是「两档同键两值」的唯一解析入口：载体只传档，不写颜色分支。</para>
    /// </summary>
    /// <param name="program">表面程序。</param>
    /// <param name="profileId">外观档（空 ⇒ 声明 active_profile）。</param>
    public static IReadOnlyDictionary<string, string> TokensForProfile(
        SurfaceProgramDeclaration program,
        string? profileId)
    {
        ArgumentNullException.ThrowIfNull(program);
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (program.Tokens.Count == 0) return map;

        var name = string.IsNullOrWhiteSpace(profileId) ? program.ActiveProfile : profileId!;
        var profile = program.EffectiveProfile(name);
        var tier = profile is not null && profile.Values.TryGetValue("ui_skin", out var skin) && skin.Length > 0
            ? skin
            : null;

        foreach (var pair in program.Tokens)
        {
            // 档位键存在但值为空 = 该档显式关闭此令牌（例如深档关闭页面装饰层，保持像素等价）。
            if (tier is not null && pair.Value.TryGetValue(tier, out var exact))
            {
                if (exact.Length > 0) map[pair.Key] = exact;
                continue;
            }

            if (pair.Value.TryGetValue("default", out var fallback) && fallback.Length > 0)
            {
                map[pair.Key] = fallback;
            }
        }

        return map;
    }

    /// <summary>构建解析上下文（纯函数；同一 program 的多次解析可只建一次，避免重复构建能力矩阵/层规格）。</summary>
    /// <param name="program">表面程序。</param>
    public static SurfaceResolveContext Prepare(SurfaceProgramDeclaration program)
    {
        ArgumentNullException.ThrowIfNull(program);
        var capabilities = CapabilityMatrix.FromStrings(
            program.Carriers.ToDictionary(
                pair => pair.Key,
                pair => (IReadOnlyDictionary<string, string>)pair.Value.Caps,
                StringComparer.Ordinal));

        var layerSpecs = new Dictionary<string, LayerSpec>(program.Layers.Count, StringComparer.Ordinal);
        var assetIssuesByLayer = new Dictionary<string, SurfaceIssue>(StringComparer.Ordinal);
        foreach (var pair in program.Layers)
        {
            var parameters = new Dictionary<string, string>(pair.Value.Params.Count, StringComparer.Ordinal);
            foreach (var parameter in pair.Value.Params)
            {
                // 资源 id → 载体无关相对路径（**单一转换点**）：未声明 ⇒ 保留 id 并可见化（按材质归集）。
                if (!string.Equals(parameter.Key, "asset", StringComparison.Ordinal))
                {
                    parameters[parameter.Key] = parameter.Value;
                    continue;
                }

                if (!program.Assets.TryGetValue(parameter.Value, out var asset))
                {
                    assetIssuesByLayer[pair.Key] = new SurfaceIssue("asset-unknown", parameter.Value);
                    parameters[parameter.Key] = parameter.Value;
                    continue;
                }

                parameters[parameter.Key] = asset.Src;
                if (!pair.Value.Params.ContainsKey("slice") && asset.Slice.Length > 0) parameters["slice"] = asset.Slice;
                if (!pair.Value.Params.ContainsKey("repeat") && asset.Repeat.Length > 0) parameters["repeat"] = asset.Repeat;
                if (!pair.Value.Params.ContainsKey("anchor") && asset.Anchor.Length > 0) parameters["anchor"] = asset.Anchor;
            }

            layerSpecs[pair.Key] = new LayerSpec(pair.Value.Name, pair.Value.Primitive, pair.Value.Z, parameters);
        }

        return new SurfaceResolveContext(capabilities, layerSpecs, assetIssuesByLayer);
    }

    private static ResolvedContainer ResolveContainer(
        SurfaceProgramDeclaration program,
        SurfaceContainerSpec container,
        SurfaceProfileSpec? profile,
        string carrierId,
        IReadOnlyDictionary<string, string> tokens,
        CapabilityMatrix capabilities,
        IReadOnlyDictionary<string, LayerSpec> layerSpecs,
        IReadOnlyDictionary<string, SurfaceIssue> assetIssuesByLayer,
        string channel)
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
            materials[slot.Key] = ResolveMaterial(
                program, carrierId, slot.Key, slot.Value, capabilities, layerSpecs, tokens, channel, assetIssuesByLayer, issues);
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

    /// <summary>
    /// 解析一个槽的材质：**委托通用内核**（层 × 能力 × 通道 × 值域 × fallback）。
    /// <para>本方法只做「声明 → 内核输入」适配与 issue 归集，不再自带层解析逻辑（单实现）。</para>
    /// </summary>
    private static ResolvedMaterial ResolveMaterial(
        SurfaceProgramDeclaration program,
        string carrierId,
        string slot,
        string materialId,
        CapabilityMatrix capabilities,
        IReadOnlyDictionary<string, LayerSpec> layerSpecs,
        IReadOnlyDictionary<string, string> tokens,
        string channel,
        IReadOnlyDictionary<string, SurfaceIssue> assetIssuesByLayer,
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

        IReadOnlyDictionary<string, string>? fallbackRefs = null;
        if (!string.IsNullOrWhiteSpace(material.Fallback)
            && program.Materials.TryGetValue(material.Fallback, out var fallbackMaterial))
        {
            fallbackRefs = fallbackMaterial.Layers;
        }

        var propertyChannel = string.Equals(channel, LayeredResolver.PropertyChannel, StringComparison.Ordinal);
        // 属性通道的 alpha 由宿主预合成成 A=255：解析期把 alpha 视为“已预合成的不透明值”，
        // 否则 glass_fill 会被 supported-opaque 值域拒绝，退回 muted_surface 黑面。
        Func<string, string?>? domainClassifier = propertyChannel
            ? value =>
            {
                var domain = SurfaceColorDomain.Classify(value);
                return string.Equals(domain, SurfaceColorDomain.Alpha, StringComparison.Ordinal)
                    ? SurfaceColorDomain.Opaque
                    : domain;
            }
            : SurfaceColorDomain.Classify;
        // 只有「控件面」槽允许在属性通道保留 fill 走到 L2；页面/宿主槽（panel_surface 等）必须保持原回落
        // （flat → 页面声明底色），否则整窗会被同一块 glass_fill 预合成色铺平（panelToPage 掉到 1.0）。
        var controlFaceSlot = string.Equals(slot, ControlFaceSlot, StringComparison.Ordinal);
        var resolution = propertyChannel && controlFaceSlot
            ? LayeredResolver.ResolveGraded(
                layerSpecs,
                material.Layers,
                capabilities,
                carrierId,
                tokenResolver: value => tokens.TryGetValue(value, out var resolved) ? resolved : value,
                fallback: material.Fallback,
                fallbackRefs: fallbackRefs,
                channel: channel,
                domainClassifier: domainClassifier)
            : LayeredResolver.Resolve(
                layerSpecs,
                material.Layers,
                capabilities,
                carrierId,
                tokenResolver: value => tokens.TryGetValue(value, out var resolved) ? resolved : value,
                fallback: material.Fallback,
                fallbackRefs: fallbackRefs,
                channel: channel,
                domainClassifier: domainClassifier);

        var issues = new List<SurfaceIssue>(resolution.Issues.Count + 1);
        // 同因多报去重：载体未声明已在表面级报过一次，此处不逐层重复（内核保持「每层可见」，去重归口调用方）。
        var carrierDeclared = capabilities.HasCarrier(carrierId);
        foreach (var issue in resolution.Issues)
        {
            if (!carrierDeclared && string.Equals(issue.Code, ResolutionIssueCodes.CarrierUndeclared, StringComparison.Ordinal))
            {
                continue;
            }

            issues.Add(new SurfaceIssue(issue.Code, issue.Detail));
        }

        foreach (var layerName in ReferencedLayers(material, program))
        {
            if (assetIssuesByLayer.TryGetValue(layerName, out var assetIssue))
            {
                issues.Add(new SurfaceIssue(assetIssue.Code, material.Name + ":" + layerName + ":" + assetIssue.Detail));
            }
        }

        foreach (var issue in issues) containerIssues.Add(issue);

        var layers = new List<ResolvedLayer>(resolution.Layers.Count);
        foreach (var layer in resolution.Layers)
        {
            // 全透明层（#RRGGBB00）在解析期剔除，不进入渲染栈：它本来就不可见，
            // 但 GDI+ 抗锯齿仍会在圆角边缘留下 1 通道级的合成残差。剔除后
            // 「某档把 token 置为全透明」= 该档像素与没有此层时逐像素一致（深档等价判据）。
            if (IsInvisible(layer)) continue;
            layers.Add(new ResolvedLayer(layer.Role, layer.Primitive, layer.Z, layer.Params));
        }

        var step = propertyChannel
            ? controlFaceSlot
                ? resolution.FallbackStep
                : resolution.FallbackUsed is null
                    ? (HasFill(resolution.Layers) ? SurfaceFallbackStep.L2SolidFace : SurfaceFallbackStep.L5Unsupported)
                    : SurfaceFallbackStep.L3ParentCompositeFace
            : resolution.FallbackUsed is null
                ? (resolution.Degraded ? SurfaceFallbackStep.L1Emulated : SurfaceFallbackStep.L0Full)
                : SurfaceFallbackStep.L2SolidFace;

        return new ResolvedMaterial(slot, materialId, resolution.FallbackUsed, layers, issues)
            with { FallbackStep = step };
    }

    /// <summary>层不可见 = 主色 alpha 为 00（只识别 <c>#RRGGBBAA</c> 的完全透明，其余原样保留）。</summary>
    private static bool IsInvisible(Modules.CapabilityResolution.ResolvedLayer layer)
        => layer.Params.TryGetValue("value", out var value)
           && value.Length == 9
           && value[0] == '#'
           && string.Equals(value.Substring(7, 2), "00", StringComparison.OrdinalIgnoreCase);

    /// <summary>层栈是否含可承载的实心面（fill/gradient）。</summary>
    private static bool HasFill(System.Collections.Generic.IReadOnlyList<Modules.CapabilityResolution.ResolvedLayer> layers)
        => layers.Any(layer =>
            string.Equals(layer.Primitive, "fill", StringComparison.Ordinal)
            || string.Equals(layer.Primitive, "gradient", StringComparison.Ordinal));

    private static IEnumerable<string> ReferencedLayers(SurfaceMaterialSpec material, SurfaceProgramDeclaration program)
    {
        foreach (var layerRef in material.Layers.Values) yield return layerRef;
        if (!string.IsNullOrWhiteSpace(material.Fallback)
            && program.Materials.TryGetValue(material.Fallback, out var fallback))
        {
            foreach (var layerRef in fallback.Layers.Values) yield return layerRef;
        }
    }
}
