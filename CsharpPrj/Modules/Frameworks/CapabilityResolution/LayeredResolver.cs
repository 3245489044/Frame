#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;

namespace Modules.CapabilityResolution;

/// <summary>已解析的一层（参数已换值；能力三态随行）。</summary>
/// <param name="Role">层角色（调用方语义，如 base/edge/shadow）。</param>
/// <param name="Primitive">图元。</param>
/// <param name="Z">叠放序。</param>
/// <param name="Params">已解析参数。</param>
/// <param name="State">该载体上的能力三态（emulated 保留；unsupported 不会出现在结果中）。</param>
public sealed record ResolvedLayer(
    string Role,
    string Primitive,
    int Z,
    IReadOnlyDictionary<string, string> Params,
    CapabilityState State);

/// <summary>一次层解析结果（层栈 + 可见问题 + 回退事实）。</summary>
/// <param name="Layers">层栈（z 升序）。</param>
/// <param name="Issues">解析问题（缺失/不支持/模拟/回退）。</param>
/// <param name="FallbackUsed">实际生效的回退名（null = 未回退）。</param>
/// <param name="Degraded">是否发生降级（缺陷层/回退）。</param>
public sealed record LayerResolution(
    IReadOnlyList<ResolvedLayer> Layers,
    IReadOnlyList<ResolutionIssue> Issues,
    string? FallbackUsed,
    bool Degraded)
{
    /// <summary>回落梯度步（L0–L5；默认 L0，旧调用方零变化）。</summary>
    public SurfaceFallbackStep FallbackStep { get; init; } = SurfaceFallbackStep.L0Full;
}

/// <summary>
/// 通用层解析器（**纯函数**）：层引用表 × 能力矩阵 × 载体 × 通道 → 已解析层栈。
/// <para>规则：层名未声明 ⇒ 剔除并记账；能力 unsupported ⇒ 剔除并记账；emulated ⇒ 保留并记账；值域越界 ⇒ 剔除并记账；z 升序唯一。</para>
/// <para>回退：主材质有降级且提供 fallback 引用表 ⇒ 用 fallback 重建层栈，并记 material-degraded。</para>
/// </summary>
public static class LayeredResolver
{
    /// <summary>兼容重载（无通道/值域参数）：等价 channel=draw（保护已烘焙调用方的二进制绑定）。</summary>
    /// <param name="layers">层定义。</param>
    /// <param name="layerRefs">角色 → 层名。</param>
    /// <param name="capabilities">能力矩阵。</param>
    /// <param name="carrier">载体 id。</param>
    /// <param name="tokenResolver">参数换值（null = 原样）。</param>
    /// <param name="fallback">回退名（空 = 无回退）。</param>
    /// <param name="fallbackRefs">回退层引用。</param>
    public static LayerResolution Resolve(
        IReadOnlyDictionary<string, LayerSpec> layers,
        IReadOnlyDictionary<string, string> layerRefs,
        CapabilityMatrix capabilities,
        string carrier,
        Func<string, string>? tokenResolver,
        string? fallback,
        IReadOnlyDictionary<string, string>? fallbackRefs)
        => Resolve(layers, layerRefs, capabilities, carrier, tokenResolver, fallback, fallbackRefs,
            CapabilityMatrix.DefaultChannel, domainClassifier: null);

    /// <summary>解析一层引用表。</summary>
    /// <param name="layers">层定义（name → 规格）。</param>
    /// <param name="layerRefs">角色 → 层名。</param>
    /// <param name="capabilities">能力矩阵。</param>
    /// <param name="carrier">载体 id。</param>
    /// <param name="tokenResolver">参数换值（null = 原样）。</param>
    /// <param name="fallback">回退名（空 = 无回退）。</param>
    /// <param name="fallbackRefs">回退层引用（与 fallback 同时提供才生效）。</param>
    /// <param name="channel">通道（写面坐标；默认 <see cref="CapabilityMatrix.DefaultChannel"/>）。</param>
    /// <param name="domainClassifier">值域分类（对已换值的 value 参数；null = 不检查）。</param>
    public static LayerResolution Resolve(
        IReadOnlyDictionary<string, LayerSpec> layers,
        IReadOnlyDictionary<string, string> layerRefs,
        CapabilityMatrix capabilities,
        string carrier,
        Func<string, string>? tokenResolver = null,
        string? fallback = null,
        IReadOnlyDictionary<string, string>? fallbackRefs = null,
        string channel = CapabilityMatrix.DefaultChannel,
        Func<string, string?>? domainClassifier = null)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(layerRefs);
        ArgumentNullException.ThrowIfNull(capabilities);

        var direct = ResolveOnce(layers, layerRefs, capabilities, carrier, tokenResolver, channel, domainClassifier);
        if (!direct.Degraded || string.IsNullOrWhiteSpace(fallback) || fallbackRefs is null)
        {
            return direct;
        }

        var issues = new List<ResolutionIssue>(direct.Issues)
        {
            new(ResolutionIssueCodes.MaterialDegraded, fallback!),
        };
        var fallbackResolution = ResolveOnce(layers, fallbackRefs, capabilities, carrier, tokenResolver, channel, domainClassifier);
        issues.AddRange(fallbackResolution.Issues);

        return new LayerResolution(
            fallbackResolution.Layers,
            issues,
            fallback,
            Degraded: true);
    }

    /// <summary>属性通道 id（与声明面 caps 键一致；本类只做通道名判定，不依赖宿主）。</summary>
    public const string PropertyChannel = "property";

    /// <summary>
    /// 带回落梯度的解析（**属性通道专用**）：降级但 direct 仍含 fill/gradient ⇒ 保留 direct 层并记 L2，
    /// 不再把玻璃材质换成 <c>flat</c>/<c>row_surface</c> 实色；direct 无 fill 才使用 fallback。
    /// <para>draw/HTML 仍走 <see cref="Resolve"/>，语义零变化。</para>
    /// </summary>
    /// <param name="layers">层定义。</param>
    /// <param name="layerRefs">角色 → 层名。</param>
    /// <param name="capabilities">能力矩阵。</param>
    /// <param name="carrier">载体 id。</param>
    /// <param name="tokenResolver">参数换值（null = 原样）。</param>
    /// <param name="fallback">回退名（空 = 无回退）。</param>
    /// <param name="fallbackRefs">回退层引用。</param>
    /// <param name="channel">通道（属性通道 = <see cref="PropertyChannel"/>）。</param>
    /// <param name="domainClassifier">值域分类（null = 不检查）。</param>
    public static LayerResolution ResolveGraded(
        IReadOnlyDictionary<string, LayerSpec> layers,
        IReadOnlyDictionary<string, string> layerRefs,
        CapabilityMatrix capabilities,
        string carrier,
        Func<string, string>? tokenResolver,
        string? fallback,
        IReadOnlyDictionary<string, string>? fallbackRefs,
        string channel,
        Func<string, string?>? domainClassifier)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(layerRefs);
        ArgumentNullException.ThrowIfNull(capabilities);

        var direct = ResolveOnce(layers, layerRefs, capabilities, carrier, tokenResolver, channel, domainClassifier);
        var propertyChannel = string.Equals(channel, PropertyChannel, StringComparison.Ordinal);
        if (!direct.Degraded)
        {
            // 属性通道永远是“预合成实心面”，即使无降级也只能到 L2（不是真玻璃）。
            var step = propertyChannel
                ? SurfaceFallbackStepPolicy.PropertySolidFace(direct.Layers)
                : SurfaceFallbackStepPolicy.Draw(degraded: false, fallbackUsed: null);
            return direct with { FallbackStep = step };
        }

        // 保留 direct fill：属性通道的 fill 会在宿主合成期叠到父级不透明底，仍是玻璃族实心面（L2）。
        if (SurfaceFallbackStepPolicy.HasSolidFace(direct.Layers))
        {
            var step = propertyChannel
                ? SurfaceFallbackStepPolicy.SolidFaceOrUnsupported(hasSolidFace: true)
                : SurfaceFallbackStepPolicy.Draw(degraded: true, fallbackUsed: null);
            return direct with { FallbackStep = step };
        }

        if (!string.IsNullOrWhiteSpace(fallback) && fallbackRefs is not null)
        {
            var issues = new List<ResolutionIssue>(direct.Issues)
            {
                new(ResolutionIssueCodes.MaterialDegraded, fallback!),
            };
            var fallbackResolution = ResolveOnce(
                layers, fallbackRefs, capabilities, carrier, tokenResolver, channel, domainClassifier);
            issues.AddRange(fallbackResolution.Issues);
            var step = SurfaceFallbackStepPolicy.PropertySolidFace(fallbackResolution.Layers);
            return new LayerResolution(
                fallbackResolution.Layers,
                issues,
                fallback,
                Degraded: true)
            {
                FallbackStep = step,
            };
        }

        return direct with { FallbackStep = SurfaceFallbackStepPolicy.SolidFaceOrUnsupported(hasSolidFace: false) };
    }

    private static LayerResolution ResolveOnce(
        IReadOnlyDictionary<string, LayerSpec> layers,
        IReadOnlyDictionary<string, string> layerRefs,
        CapabilityMatrix capabilities,
        string carrier,
        Func<string, string>? tokenResolver,
        string channel,
        Func<string, string?>? domainClassifier)
    {
        var resolved = new List<ResolvedLayer>();
        var issues = new List<ResolutionIssue>();
        var degraded = false;

        foreach (var reference in layerRefs
                     .OrderBy(pair => LayerZ(layers, pair.Value))
                     .ThenBy(pair => pair.Value, StringComparer.Ordinal))
        {
            if (!layers.TryGetValue(reference.Value, out var layer))
            {
                issues.Add(new ResolutionIssue(ResolutionIssueCodes.LayerUnknown, reference.Key + ":" + reference.Value));
                degraded = true;
                continue;
            }

            if (!capabilities.HasCarrier(carrier))
            {
                issues.Add(new ResolutionIssue(ResolutionIssueCodes.CarrierUndeclared, carrier));
                degraded = true;
                continue;
            }

            var spec = capabilities.SpecOf(carrier, channel, layer.Primitive);
            if (!spec.Supported)
            {
                issues.Add(new ResolutionIssue(
                    ResolutionIssueCodes.Unsupported,
                    reference.Key + ":" + layer.Name + ":" + layer.Primitive));
                degraded = true;
                continue;
            }

            if (spec.State == CapabilityState.Emulated)
            {
                issues.Add(new ResolutionIssue(
                    ResolutionIssueCodes.Emulated,
                    reference.Key + ":" + layer.Name + ":" + layer.Primitive));
            }

            var parameters = new Dictionary<string, string>(layer.Params.Count, StringComparer.Ordinal);
            foreach (var pair in layer.Params)
            {
                parameters[pair.Key] = tokenResolver is null ? pair.Value : tokenResolver(pair.Value);
            }

            // 值域判定（唯一入口）：静态 Domain 优先，其次对已换值的 value 参数分类。
            var domain = layer.Domain;
            if (string.IsNullOrEmpty(domain) && domainClassifier is not null
                && parameters.TryGetValue("value", out var value))
            {
                domain = domainClassifier(value);
            }

            // fail-closed：通道声明了受限值域（非 AnyDomain）却无法分类该值 ⇒ 判越界（禁静默落到载体）。
            if (spec.IsRestricted && string.IsNullOrEmpty(domain) && parameters.ContainsKey("value"))
            {
                issues.Add(new ResolutionIssue(
                    ResolutionIssueCodes.ValueOutOfDomain,
                    reference.Key + ":" + layer.Name + ":" + layer.Primitive + ":unclassified"));
                degraded = true;
                continue;
            }

            if (!spec.Accepts(domain))
            {
                issues.Add(new ResolutionIssue(
                    ResolutionIssueCodes.ValueOutOfDomain,
                    reference.Key + ":" + layer.Name + ":" + layer.Primitive + ":" + domain));
                degraded = true;
                continue;
            }

            resolved.Add(new ResolvedLayer(reference.Key, layer.Primitive, layer.Z, parameters, spec.State));
        }

        return new LayerResolution(resolved, issues, FallbackUsed: null, Degraded: degraded);
    }

    private static int LayerZ(IReadOnlyDictionary<string, LayerSpec> layers, string layerName)
        => layers.TryGetValue(layerName, out var layer) ? layer.Z : int.MaxValue;
}
