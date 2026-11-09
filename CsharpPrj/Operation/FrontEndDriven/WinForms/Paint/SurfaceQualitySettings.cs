#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Threading;
using FrameworkAbstractions.ComponentRegistry;
using FrontEndDriven.Abstractions;
using FrontEndDriven.Diagnostics;
using Share.Conversation.Rendering.Surface;
using UiBackend;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 画质设置（用户态）：与窗体尺寸/分隔比例**同一存储件**（数据根 <c>\UI\ui-layout-state.json</c> 的 <c>panels</c> 段）。
/// <para>值是「覆盖项」：缺项 ⇒ 回退表面声明（surface.generated.json）的默认值，不写回配置。</para>
/// </summary>
internal static class SurfaceQualitySettings
{
    /// <summary>面板作用域（与 UI 布局 scope 命名空间隔离）。</summary>
    internal const string Scope = "panel/graphics";

    internal const string KeyProfile = "profile";
    internal const string KeyEngine = "engine";
    internal const string KeyQuality = "quality";
    internal const string KeyTier = "tier";
    internal const string KeyAmbient = "ambient";
    internal const string KeyRadial = "radial";
    internal const string KeyEdge = "edge";

    /// <summary>已持久化的覆盖项（null = 未设置，走声明缺省）。档位不在此列：它是切档请求，不是装配覆盖。</summary>
    internal sealed record Values(
        bool? EngineEnabled,
        string? EngineQuality,
        GlassTier? Tier,
        bool? AmbientEnabled,
        float? RadialAlpha,
        float? EdgeWidth)
    {
        internal static readonly Values Empty = new(null, null, null, null, null, null);
    }

    /// <summary>
    /// 用户主动切换时要应用的档（草稿优先）：<c>surface.values.profile</c>（兼容读旧 <c>panels.profile</c>）；
    /// 未改过下拉时回落已提交 <see cref="CommittedProfile"/>。
    /// <para>只由 <see cref="SurfaceAppearanceSwitch"/> 消费；它表达"用户请求的档"，不是"当前档"。</para>
    /// </summary>
    internal static string? RequestedProfile()
    {
        var store = AppServiceProvider.Resolve<IUiLayoutStateStore>();
        if (store is null)
            return CommittedProfile();

        // 新：surface.values.profile（唯一用户可写节点）。
        if (store.TryGetSurfaceValues(out var surfaceValues)
            && surfaceValues.TryGetValue(KeyProfile, out var surfaceValue)
            && !string.IsNullOrWhiteSpace(surfaceValue))
        {
            return surfaceValue.Trim();
        }

        // 升级期兼容：老构建只写过 panels.profile。
        if (store.TryGetPanelValues(Scope, out var raw)
            && raw.TryGetValue(KeyProfile, out var value)
            && !string.IsNullOrWhiteSpace(value))
        {
            return value.Trim();
        }

        return CommittedProfile();
    }

    /// <summary>
    /// 已提交的档（启动装配的唯一依据）：优先 <c>surface.profile</c>；
    /// 旧文件无 <c>surface</c> 节点时，一次性兼容读 <c>panels.profile</c>。
    /// </summary>
    internal static string? CommittedProfile()
    {
        var store = AppServiceProvider.Resolve<IUiLayoutStateStore>();
        if (store is null)
            return null;

        if (store.TryGetSurfaceSelection(out var selection) && !string.IsNullOrWhiteSpace(selection.Profile))
            return selection.Profile.Trim();

        // 升级期兼容：老构建只写过 panels.profile（当时它就是"用户选择"）。
        return store.TryGetPanelValues(Scope, out var raw)
               && raw.TryGetValue(KeyProfile, out var value)
               && !string.IsNullOrWhiteSpace(value)
            ? value.Trim()
            : null;
    }

    /// <summary>读取持久化覆盖项（surface 节点优先，兼容旧 panels；无记录 ⇒ 全 null）。</summary>
    internal static Values Load()
    {
        var store = AppServiceProvider.Resolve<IUiLayoutStateStore>();
        if (store is null)
            return Values.Empty;

        // 升级期兼容：老值在 panels[panel/graphics]；surface 覆盖逐键胜出（可只迁移部分键）。
        var merged = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (store.TryGetPanelValues(Scope, out var legacy))
        {
            foreach (var pair in legacy) merged[pair.Key] = pair.Value;
        }

        if (store.TryGetSurfaceValues(out var surface))
        {
            foreach (var pair in surface) merged[pair.Key] = pair.Value;
        }

        if (merged.Count == 0)
            return Values.Empty;

        var raw = (IReadOnlyDictionary<string, string>)merged;

        string? Get(string key)
            => raw.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

        return new Values(
            EngineEnabled: ParseBool(Get(KeyEngine)),
            EngineQuality: Get(KeyQuality),
            Tier: ParseTier(Get(KeyTier)),
            AmbientEnabled: ParseBool(Get(KeyAmbient)),
            RadialAlpha: ParseFloat(Get(KeyRadial)),
            EdgeWidth: ParseFloat(Get(KeyEdge)));
    }

    /// <summary>按覆盖项改写令牌表（只覆盖已设置的项；其余保持声明值）。</summary>
    internal static Dictionary<string, string> MergeTokens(
        IReadOnlyDictionary<string, string> declared, Values values)
        => Share.SurfacePaint.Materials.SurfaceAmbientTokenMerge.Merge(
            declared,
            new Share.SurfacePaint.Materials.SurfaceAmbientOverride(
                values.AmbientEnabled, values.RadialAlpha, values.EdgeWidth));

    /// <summary>
    /// 覆盖项的令牌形态（**唯一**发布给选择源的形态）：两载体与缓存键都经
    /// <c>SurfaceSelectionProvider.Overrides</c> 读到同一份参与解析的覆盖。
    /// </summary>
    internal static IReadOnlyDictionary<string, string> OverridesOf(Values values)
    {
        var map = new Dictionary<string, string>(StringComparer.Ordinal);
        if (values.AmbientEnabled is { } ambient) map["ambient_enabled"] = ambient ? "1" : "0";
        if (values.RadialAlpha is { } radial)
            map["ambient_radial_alpha"] = radial.ToString("R", CultureInfo.InvariantCulture);
        if (values.EdgeWidth is { } edge)
            map["ambient_edge_width"] = edge.ToString("R", CultureInfo.InvariantCulture);
        return map;
    }

    private static bool? ParseBool(string? text)
        => text?.ToLowerInvariant() switch
        {
            "on" or "true" or "1" => true,
            "off" or "false" or "0" => false,
            _ => null,
        };

    private static float? ParseFloat(string? text)
        => float.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value) && float.IsFinite(value)
            ? value
            : null;

    private static int _unsupportedTierLogged;

    private static GlassTier? ParseTier(string? text)
    {
        // tier=Unsupported 是「声明不支持的画质上限」，不是可用选择：视为未设置，回落声明默认。
        if (string.Equals(text?.Trim(), "unsupported", StringComparison.OrdinalIgnoreCase))
        {
            if (Interlocked.Exchange(ref _unsupportedTierLogged, 1) == 0)
            {
                FrontEndDrivenLog.Log("SurfaceQualitySettings_tier_unsupported_ignored", new
                {
                    scope = Scope,
                    value = text
                });
            }

            return null;
        }

        return text?.ToLowerInvariant() switch
        {
            "t0" => GlassTier.T0,
            "t1" => GlassTier.T1,
            "t2" => GlassTier.T2,
            "t3" => GlassTier.T3,
            _ => null,
        };
    }
}
