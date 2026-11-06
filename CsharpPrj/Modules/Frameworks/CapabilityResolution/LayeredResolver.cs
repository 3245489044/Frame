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
    bool Degraded);

/// <summary>
/// 通用层解析器（**纯函数**）：层引用表 × 能力矩阵 × 载体 → 已解析层栈。
/// <para>规则：层名未声明 ⇒ 剔除并记账；unsupported ⇒ 剔除并记账；emulated ⇒ 保留并记账；z 升序唯一。</para>
/// <para>回退：主材质有降级且提供 fallback 引用表 ⇒ 用 fallback 重建层栈，并记 material-degraded。</para>
/// </summary>
public static class LayeredResolver
{
    /// <summary>解析一层引用表。</summary>
    /// <param name="layers">层定义（name → 规格）。</param>
    /// <param name="layerRefs">角色 → 层名。</param>
    /// <param name="capabilities">能力矩阵。</param>
    /// <param name="carrier">载体 id。</param>
    /// <param name="tokenResolver">参数换值（null = 原样）。</param>
    /// <param name="fallback">回退名（空 = 无回退）。</param>
    /// <param name="fallbackRefs">回退层引用（与 fallback 同时提供才生效）。</param>
    public static LayerResolution Resolve(
        IReadOnlyDictionary<string, LayerSpec> layers,
        IReadOnlyDictionary<string, string> layerRefs,
        CapabilityMatrix capabilities,
        string carrier,
        Func<string, string>? tokenResolver = null,
        string? fallback = null,
        IReadOnlyDictionary<string, string>? fallbackRefs = null)
    {
        ArgumentNullException.ThrowIfNull(layers);
        ArgumentNullException.ThrowIfNull(layerRefs);
        ArgumentNullException.ThrowIfNull(capabilities);

        var direct = ResolveOnce(layers, layerRefs, capabilities, carrier, tokenResolver);
        if (!direct.Degraded || string.IsNullOrWhiteSpace(fallback) || fallbackRefs is null)
        {
            return direct;
        }

        var issues = new List<ResolutionIssue>(direct.Issues)
        {
            new(ResolutionIssueCodes.MaterialDegraded, fallback!),
        };
        var fallbackResolution = ResolveOnce(layers, fallbackRefs, capabilities, carrier, tokenResolver);
        issues.AddRange(fallbackResolution.Issues);

        return new LayerResolution(
            fallbackResolution.Layers,
            issues,
            fallback,
            Degraded: true);
    }

    private static LayerResolution ResolveOnce(
        IReadOnlyDictionary<string, LayerSpec> layers,
        IReadOnlyDictionary<string, string> layerRefs,
        CapabilityMatrix capabilities,
        string carrier,
        Func<string, string>? tokenResolver)
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

            var state = capabilities.StateOf(carrier, layer.Primitive);
            if (state == CapabilityState.Unsupported)
            {
                issues.Add(new ResolutionIssue(
                    ResolutionIssueCodes.Unsupported,
                    reference.Key + ":" + layer.Name + ":" + layer.Primitive));
                degraded = true;
                continue;
            }

            if (state == CapabilityState.Emulated)
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

            resolved.Add(new ResolvedLayer(reference.Key, layer.Primitive, layer.Z, parameters, state));
        }

        return new LayerResolution(resolved, issues, FallbackUsed: null, Degraded: degraded);
    }

    private static int LayerZ(IReadOnlyDictionary<string, LayerSpec> layers, string layerName)
        => layers.TryGetValue(layerName, out var layer) ? layer.Z : int.MaxValue;
}
