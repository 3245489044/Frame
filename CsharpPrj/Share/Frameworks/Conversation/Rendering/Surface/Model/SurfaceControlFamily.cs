#nullable enable

using System;
using System.Collections.Generic;
using Modules.CapabilityResolution;

namespace Share.Conversation.Rendering.Surface;

/// <summary>控件族的表面策略（替代原 <c>SupportsOwnerDraw</c> 白名单与 SplitterPanel 过滤）。</summary>
public enum SurfaceStrategy
{
    /// <summary>宿主自绘（Panel/GroupBox/Label 等）。</summary>
    OwnerDrawContainer,

    /// <summary>只能走属性通道（TextBox/Button/ListView 等原生 HWND）。</summary>
    PropertyFace,

    /// <summary>原生 NC chrome，本批不接管。</summary>
    NativeSubclass,

    /// <summary>后续批次接管绘制的原生控件。</summary>
    CustomPaint,

    /// <summary>继承父级表面（SplitterPanel）。</summary>
    InheritParent,

    /// <summary>明确不支持（ToolStrip/滚动条等）。</summary>
    Unsupported,
}

/// <summary>背板来源（本批只用 Parent/None，快照仍由 SkinDecorator 现有实现提供）。</summary>
public enum SurfaceBackdropMode { Host, Parent, None }

/// <summary>边缘归属。</summary>
public enum SurfaceEdgeMode { Material, FrameHost, Native }

/// <summary>光探针归属（后续批次使用；本批保留声明）。</summary>
public enum SurfaceProbeMode { PerControl, PerHost }

/// <summary>平台 chrome 策略（A1）。</summary>
public enum SurfaceChromePolicy { Native, Replace, Unsupported }

/// <summary>回落策略：玻璃族必须 ≥ L2；非玻璃族允许 L5。</summary>
public enum SurfaceFallbackPolicy { RequireGlassFace, AllowSolid, NonGlass }

/// <summary>旧样式色在表面体系中的角色（C 类）。</summary>
public enum SurfaceStyleRole { AccentTint, NeutralHint, DirectColor, Ignore }

/// <summary>内容绘制归属（后续批次使用）。</summary>
public enum SurfaceContentKind { None, Text, Items, Glyph }

/// <summary>
/// 控件族声明（单一登记点）：策略、材质槽、背板/边缘/光归属、chrome 策略、回落策略与最低档。
/// </summary>
public sealed record SurfaceControlFamilySpec(
    string TypeKey,
    SurfaceStrategy Strategy,
    string MaterialSlot,
    bool OwnsBackColor,
    SurfaceBackdropMode BackdropMode,
    SurfaceEdgeMode EdgeMode,
    SurfaceProbeMode ProbeMode,
    SurfaceChromePolicy ChromePolicy,
    SurfaceFallbackPolicy FallbackPolicy,
    SurfaceStyleRole StyleRole,
    SurfaceContentKind Content,
    SurfaceFallbackStep MinimumStep);

/// <summary>
/// 控件族注册表（取代“白名单 + SplitterPanel 过滤”的自造限制）。
/// <para>未登记 ⇒ 沿用旧白名单（零回归）；登记后按族分派。</para>
/// </summary>
public static class SurfaceControlFamilyRegistry
{
    private static readonly object Gate = new();
    private static readonly Dictionary<string, SurfaceControlFamilySpec> ByType = new(StringComparer.OrdinalIgnoreCase);
    private static bool _defaultsLoaded;

    /// <summary>登记/覆盖一个族（测试与负对照也走这里）。</summary>
    public static void Register(SurfaceControlFamilySpec spec)
    {
        ArgumentNullException.ThrowIfNull(spec);
        EnsureDefaults();
        lock (Gate) ByType[spec.TypeKey] = spec;
    }

    /// <summary>按类型键取族（未登记 ⇒ null）。</summary>
    public static SurfaceControlFamilySpec? For(string? typeKey)
    {
        if (typeKey is not { Length: > 0 }) return null;
        EnsureDefaults();
        lock (Gate) return ByType.TryGetValue(typeKey, out var spec) ? spec : null;
    }


    /// <summary>全部登记（诊断/门禁扫描）。</summary>
    public static IReadOnlyCollection<SurfaceControlFamilySpec> Families
    {
        get
        {
            EnsureDefaults();
            lock (Gate) return ByType.Values;
        }
    }

    /// <summary>载入默认族（幂等；Install 前也由 <see cref="For"/> 兜底触发）。</summary>
    public static void EnsureDefaults()
    {
        if (_defaultsLoaded) return;
        lock (Gate)
        {
            if (_defaultsLoaded) return;
            RegisterCore();
            _defaultsLoaded = true;
        }
    }

    private static void RegisterCore()
    {
        foreach (var key in new[] { "panel", "flowlayoutpanel", "tablelayoutpanel", "groupbox", "usercontrol", "label" })
        {
            ByType[key] = new SurfaceControlFamilySpec(
                key, SurfaceStrategy.OwnerDrawContainer, "panel_surface", true,
                SurfaceBackdropMode.Host, SurfaceEdgeMode.Material, SurfaceProbeMode.PerControl,
                SurfaceChromePolicy.Native, SurfaceFallbackPolicy.RequireGlassFace,
                SurfaceStyleRole.Ignore, SurfaceContentKind.Text, SurfaceFallbackStep.L2SolidFace);
        }

        foreach (var key in new[] { "textbox", "listview", "listbox", "combobox", "treeview" })
        {
            ByType[key] = new SurfaceControlFamilySpec(
                key, SurfaceStrategy.PropertyFace, "control_face", true,
                SurfaceBackdropMode.None, SurfaceEdgeMode.Material, SurfaceProbeMode.PerControl,
                SurfaceChromePolicy.Native, SurfaceFallbackPolicy.RequireGlassFace,
                SurfaceStyleRole.NeutralHint, SurfaceContentKind.Text, SurfaceFallbackStep.L2SolidFace);
        }

        foreach (var key in new[] { "button", "checkbox" })
        {
            // C1：自绘外壳族（只画背景/边框，内容仍由原生控件绘制；不接管编辑区/弹出列表）。
            ByType[key] = new SurfaceControlFamilySpec(
                key, SurfaceStrategy.CustomPaint, "control_face", true,
                SurfaceBackdropMode.None, SurfaceEdgeMode.Material, SurfaceProbeMode.PerControl,
                SurfaceChromePolicy.Native, SurfaceFallbackPolicy.RequireGlassFace,
                SurfaceStyleRole.AccentTint, SurfaceContentKind.Glyph, SurfaceFallbackStep.L2SolidFace);
        }

        foreach (var key in new[] { "splitcontainer", "splitterpanel" })
        {
            ByType[key] = new SurfaceControlFamilySpec(
                // 结构宿主（不是卡片）：host_surface 在各档都映射 flat（页面声明底色），
                // 避免 SplitContainer 面板被画成 panel_frost 而把整窗铺平成同一块玻璃面（panelToPage → 1.0）。
                // 边缘由 SurfaceFrameHost 补：host_surface 各档都映射 flat（无 stroke），
                // 若仍声明 Material，宿主改平后会连 1px 玻璃边一起丢掉（borderToPanel 归零）。
                key, SurfaceStrategy.OwnerDrawContainer, "host_surface", true,
                SurfaceBackdropMode.Parent, SurfaceEdgeMode.FrameHost, SurfaceProbeMode.PerControl,
                SurfaceChromePolicy.Native, SurfaceFallbackPolicy.RequireGlassFace,
                SurfaceStyleRole.Ignore, SurfaceContentKind.None, SurfaceFallbackStep.L2SolidFace);
        }

        foreach (var key in new[]
                 {
                     "statusstrip", "toolstrip", "menustrip", "contextmenustrip",
                     "progressbar", "trackbar", "numericupdown", "datetimepicker",
                 })
        {
            ByType[key] = new SurfaceControlFamilySpec(
                key, SurfaceStrategy.Unsupported, "row_surface", false,
                SurfaceBackdropMode.None, SurfaceEdgeMode.Native, SurfaceProbeMode.PerControl,
                SurfaceChromePolicy.Unsupported, SurfaceFallbackPolicy.NonGlass,
                SurfaceStyleRole.DirectColor, SurfaceContentKind.None, SurfaceFallbackStep.L5Unsupported);
        }

        // A1：平台 NC chrome 登记为 Native/Unsupported，只做可见化，不承诺玻璃。
        foreach (var key in new[] { "listview-header", "scrollbar", "scrollbar-corner", "combobox-popup", "splitter", "titlebar" })
        {
            ByType[key] = new SurfaceControlFamilySpec(
                key, SurfaceStrategy.Unsupported, "row_surface", false,
                SurfaceBackdropMode.None, SurfaceEdgeMode.Native, SurfaceProbeMode.PerControl,
                SurfaceChromePolicy.Native, SurfaceFallbackPolicy.NonGlass,
                SurfaceStyleRole.DirectColor, SurfaceContentKind.None, SurfaceFallbackStep.L5Unsupported);
        }
    }

}
