#nullable enable

using System;
using System.Collections.Generic;
using System.Globalization;
using FrameworkAbstractions.ComponentRegistry;
using FrontEndDriven.Abstractions;
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

    /// <summary>已持久化的覆盖项（null = 未设置，走声明缺省）。</summary>
    internal sealed record Values(
        string? Profile,
        bool? EngineEnabled,
        string? EngineQuality,
        GlassTier? Tier,
        bool? AmbientEnabled,
        float? RadialAlpha,
        float? EdgeWidth)
    {
        internal static readonly Values Empty = new(null, null, null, null, null, null, null);
    }

    /// <summary>读取持久化覆盖项（未装配存储/无记录 ⇒ 全 null）。</summary>
    internal static Values Load()
    {
        var store = AppServiceProvider.Resolve<IUiLayoutStateStore>();
        if (store is null || !store.TryGetPanelValues(Scope, out var raw))
            return Values.Empty;

        string? Get(string key)
            => raw.TryGetValue(key, out var value) && !string.IsNullOrWhiteSpace(value) ? value.Trim() : null;

        return new Values(
            Profile: Get(KeyProfile),
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
    {
        var tokens = new Dictionary<string, string>(declared, StringComparer.OrdinalIgnoreCase);

        if (values.AmbientEnabled is { } ambient)
            tokens["ambient_enabled"] = ambient ? "1" : "0";
        if (values.RadialAlpha is { } radial)
            tokens["ambient_radial_alpha"] = radial.ToString("0.###", CultureInfo.InvariantCulture);
        if (values.EdgeWidth is { } edge)
            tokens["ambient_edge_width"] = edge.ToString("0.###", CultureInfo.InvariantCulture);

        return tokens;
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

    private static GlassTier? ParseTier(string? text)
        => text?.ToLowerInvariant() switch
        {
            "unsupported" => GlassTier.Unsupported,
            "t0" => GlassTier.T0,
            "t1" => GlassTier.T1,
            "t2" => GlassTier.T2,
            "t3" => GlassTier.T3,
            _ => null,
        };
}
