#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using FrontEndDriven.Diagnostics;
using Share.Conversation.Rendering.Surface;
using Share.Conversation.Rendering.Surface.Gather;
using Share.Conversation.Rendering.Surface.Selection;
using Share.SurfacePaint;
using Share.SurfacePaint.Ambient;
using UiBackend;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 外观安装器（**宿主无关的唯一装配入口**）：吃表面声明 → 解析材质目录 + 全局回退材质 → 点亮 owner-draw。
/// <para>宿主只需一行：<c>SkinAppearanceInstaller.Install(program, profileId)</c>；运行期换档走唯一入口 <see cref="SurfaceAppearanceSwitch"/>。</para>
/// <para>声明不可用 ⇒ 卸载并回到纯属性着色（零回归）；档位由调用方显式给出，禁代码硬编码。</para>
/// </summary>
public static class SkinAppearanceInstaller
{
    /// <summary>区域主容器槽 → 必须具备的最低装饰能力（叶子槽不在此列）。</summary>
    private static readonly (string Slot, SurfaceDecoration Required)[] StructuralSlots =
    {
        ("host_surface", SurfaceDecoration.PageField | SurfaceDecoration.Ring),
        ("panel_surface", SurfaceDecoration.Chrome | SurfaceDecoration.Ring),
        ("sidebar_surface", SurfaceDecoration.Chrome | SurfaceDecoration.Ring),
        ("composer_surface", SurfaceDecoration.Chrome | SurfaceDecoration.Ring),
        ("stream_surface", SurfaceDecoration.Ring),
    };

    /// <summary>按声明安装（幂等；可重复调用以换档）。</summary>
    /// <param name="program">表面声明（<c>surface.generated.json</c> 解析结果）。</param>
    /// <param name="profileId">外观档（**显式必填**：档位只有一个来源，见 <see cref="SurfaceAppearanceSwitch"/>）。</param>
    /// <param name="carrierId">载体 id（WinForms = <c>winforms</c>）。</param>
    /// <returns>是否已启用 owner-draw。</returns>
    public static bool Install(
        SurfaceProgramDeclaration program,
        string profileId,
        string carrierId = "winforms")
    {
        if (program is null || !program.IsDeclared || string.IsNullOrWhiteSpace(profileId))
        {
            Uninstall();
            return false;
        }

        var profile = profileId.Trim();
        // 画质设置（用户态，数据根同 UI 布局）：只覆盖引擎/质量/玻璃/光场；档位不在此处覆盖。
        var quality = SurfaceQualitySettings.Load();
        // 覆盖项唯一发布点：两载体与缓存键都从选择源读到同一份「参与解析的覆盖」。
        SurfaceSelectionProvider.Current.Overrides = SurfaceQualitySettings.OverridesOf(quality);

        // 生效快照（唯一装配入口）：声明 + 档 + 覆盖 ⇒ 全视图/令牌/设置/玻璃/引擎光同源。
        var bundle = SurfacePlanBuilder.Build(
            SurfaceProgram.From(program),
            carrierId,
            SurfaceSelectionProvider.Current.Select("render-surface", carrierId),
            WinFormsPropertyProjector.ChannelId,
            profileOverride: profile);
        var byKey = new Dictionary<string, IReadOnlyDictionary<string, ResolvedMaterial>>(StringComparer.Ordinal);
        var byKeyProperty = new Dictionary<string, IReadOnlyDictionary<string, ResolvedMaterial>>(StringComparer.Ordinal);

        // 能力矩阵/层规格只建一次：档级单槽查询（page/role slot）共用同一上下文。
        var context = SurfaceResolver.Prepare(program);
        var pageBackground = ResolvePageColor(program, context, carrierId, bundle.Tokens);
        foreach (var view in bundle.Views.Values)
        {
            foreach (var container in view.Containers)
            {
                if (container.Materials.Count == 0) continue;
                byKey[container.Id] = container.Materials;
                if (!string.Equals(container.Key, container.Id, StringComparison.Ordinal))
                    byKey.TryAdd(container.Key, container.Materials);
            }
        }

        foreach (var view in bundle.PropertyViews.Values)
        {
            foreach (var container in view.Containers)
            {
                if (container.Materials.Count == 0) continue;
                byKeyProperty[container.Id] = container.Materials;
                if (!string.Equals(container.Key, container.Id, StringComparison.Ordinal))
                    byKeyProperty.TryAdd(container.Key, container.Materials);
            }
        }

        SkinMaterialCatalog.SetPageBackground(pageBackground);
        // 页面装饰层：唯一取值来源 = 当前档令牌（浅档主/深档备），代码端零颜色分支。
        var ambient = SurfaceAmbientTokens.FromTokens(bundle.Tokens);
        SurfaceAmbient.Configure(ambient);
        // G-1 宿主合成真值登记（唯一装配入口）：能力/预算/声明上限；背景由载体快照在绘制期补登。
        // Q2 默认 T0、Q6 默认 balanced（G-6 落点：只改这里的取值，不改 Clamp）。
        SurfaceHostFacts.DeclareComposition(new SurfaceCompositionCapability(
            SurfaceCompositionMode.EngineComposition,
            SurfaceAlphaMode.Premultiplied,
            SupportsOwnerDraw: true,
            SupportsGpuComposition: true,
            SupportsDesktopBackdrop: false,
            "winforms-owner-draw"));
        SurfaceHostFacts.DeclareBudget(SurfaceFrameBudget.Default);
        SurfaceHostFacts.DeclareTier(quality.Tier ?? GlassTier.T0);
        // 玻璃形态：声明表唯一值源（tier 参数/染色），选层不再按 tier 硬编码。
        SurfaceHostFacts.DeclareGlassLook(bundle.GlassLook);
        // 2026-11-08：端口装配（引擎/日志/兼容层）走唯一装配点，避免与静态构造器两处接线。
        WinFormsSurfacePorts.Configure();
        // 引擎帧开关/质量档（配置态，非代码分支；缺失 ⇒ 默认 on/balanced）；用户设置优先。
        var engineEnabled = !bundle.Settings.TryGetValue("surface_engine", out var engineSwitch)
            || !string.Equals(engineSwitch, "off", StringComparison.OrdinalIgnoreCase);
        if (quality.EngineEnabled is { } engineOverride)
            engineEnabled = engineOverride;
        SurfaceFramePump.Enabled = engineEnabled;

        var engineQuality = bundle.Settings.TryGetValue("surface_engine_quality", out var declaredQuality)
            ? declaredQuality
            : null;
        if (!string.IsNullOrWhiteSpace(quality.EngineQuality))
            engineQuality = quality.EngineQuality;
        // 覆盖项被清空 ⇒ 回到声明默认（原先只在非空时赋值，清空设置后旧档位残留）。
        SurfaceFramePump.Quality = string.IsNullOrWhiteSpace(engineQuality) ? "balanced" : engineQuality!;
        // 引擎光 look 子集：生效快照唯一来源，泵只负责把 JSON 交给引擎（引擎侧仍可派生缺省字段）。
        SurfaceLightPump.Look = bundle.EngineLight;
        // 记账：开了装饰却把结构主容器映射到未声明能力的材质 ⇒ 落日志（禁静默变成「没有装饰」）。
        var decorUndeclared = ambient.HasScene
            ? string.Join(",", StructuralSlots.Where(required =>
                SurfaceResolver.ResolveProfileSlot(program, context, profile, carrierId, required.Slot) is not { } decorMaterial
                || (decorMaterial.Decoration & required.Required) != required.Required)
                .Select(required => required.Slot))
            : string.Empty;
        var global = SelectGlobal(byKey, "panel_surface");
        // 属性通道全局回退：优先声明档的 row_surface（实心 muted 材质），缺失才回落面板槽（零回归）。
        var globalProperty = SurfaceResolver.ResolveProfileSlot(
                                program, context, profile, carrierId, "row_surface",
                                WinFormsPropertyProjector.ChannelId)
                            ?? SelectGlobal(byKeyProperty, "panel_surface");
        // B 类控件族的属性材质：control_face（glass_fill + panel_edge），缺失时保持旧全局回退。
        var controlFace = SurfaceResolver.ResolveProfileSlot(
            program, context, profile, carrierId, "control_face", WinFormsPropertyProjector.ChannelId);
        var familyProperty = new Dictionary<string, ResolvedMaterial>(StringComparer.Ordinal);
        if (controlFace is not null) familyProperty["control_face"] = controlFace;
        // 结构宿主（SplitContainer/SplitterPanel）的绘制材质：host_surface（各档 flat = 页面底色）。
        // 容器派生槽是角色槽（content→panel_surface→panel_frost），不含 host_surface；必须档级单取，
        // 否则宿主沿用卡片玻璃面 → 整窗铺平成同一块色（page_gap 与 glass_face 同值，panelToPage=1.0）。
        var hostSurface = SurfaceResolver.ResolveProfileSlot(program, context, profile, carrierId, "host_surface");
        var familyDraw = new Dictionary<string, ResolvedMaterial>(StringComparer.Ordinal);
        if (hostSurface is not null) familyDraw["host_surface"] = hostSurface;
        // C1：自绘外壳族（Button/CheckBox）的 draw 通道材质 = control_face（与属性面同槽，不同通道）。
        var controlFaceDraw = SurfaceResolver.ResolveProfileSlot(program, context, profile, carrierId, "control_face");
        if (controlFaceDraw is not null) familyDraw["control_face"] = controlFaceDraw;

        // B4-3：绘制通道目录分发取消（像素归 Composer）；只保留属性通道目录。
        SkinMaterialCatalog.SetProperty(byKeyProperty);
        SkinMaterialCatalog.SetFamilyProperty(familyProperty);
        SkinMaterialCatalog.SetGlobalProperty(globalProperty);
        SkinDecorator.Enabled = globalProperty is not null;
        // 指针光（声明令牌单源）：weight = 峰值权重（0 = 关闭/回滚），sigma = 半径系数，boost = 高光增幅。
        var tokens = bundle.Tokens;
        var pointerWeight = SurfaceLightPointer.PointerWeight;
        var pointerSigma = SurfaceLightPointer.PointerSigma;
        var pointerBoost = SurfaceLightPointer.SpecBoost;
        if (tokens.TryGetValue("light_pointer_weight", out var weightText)
            && float.TryParse(weightText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsedWeight))
        {
            pointerWeight = parsedWeight;
        }
        if (tokens.TryGetValue("light_pointer_sigma", out var sigmaText)
            && float.TryParse(sigmaText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var parsedSigma))
        {
            pointerSigma = parsedSigma;
        }
        if (tokens.TryGetValue("light_pointer_spec_boost", out var boostText)
            && float.TryParse(boostText, System.Globalization.NumberStyles.Float,
                System.Globalization.CultureInfo.InvariantCulture, out var boost))
        {
            pointerBoost = boost;
        }
        SurfaceLightPointer.Configure(pointerWeight, pointerSigma, pointerBoost);

        // HTML 载体本地读指针（显式豁免）：默认关闭；只有令牌为 1 才允许页面内 mousemove。
        var htmlLocal = SurfaceLightPointer.HtmlLocalRead;
        if (tokens.TryGetValue("light_pointer_html_local", out var htmlText)
            && int.TryParse(htmlText, System.Globalization.NumberStyles.Integer,
                System.Globalization.CultureInfo.InvariantCulture, out var parsedHtml))
        {
            htmlLocal = parsedHtml != 0;
        }
        SurfaceLightPointer.ConfigureHtmlLocal(htmlLocal);
        FrontEndDrivenLog.Log("SkinAppearance_PointerLight", new
        {
            weight = pointerWeight,
            sigma = pointerSigma,
            specBoost = pointerBoost,
            htmlLocal,
        });
        // 换档/重装配：目录已换，已挂控件必须按新目录重挂（否则切到 show 档仍画旧 quiet 材质）。
        if (SkinDecorator.Enabled) SkinDecorator.RefreshAttached();
        else SkinDecorator.Disable();
        // 边框模型可见化：遮挡/多描边/非法宽度都在安装期落日志，避免「边被盖掉」静默。
        var border = global is null ? null : BorderSpecProjection.Inspect(global);
        SkinDiagnostics.RecordInstall(
            SkinDecorator.Enabled, profile, carrierId, program.Fingerprint, byKey.Count, global);
        // 装配可见化：unresolvedTokens>0 表示层值仍是 token 名（运行期会解析成透明），
        // 由 assert skin-binding 判红；此处落日志便于在「框架日志」直接看到。
        FrontEndDrivenLog.Log("SkinAppearance_Install", new
        {
            enabled = SkinDecorator.Enabled,
            profile,
            carrier = carrierId,
            catalog = byKey.Count,
            catalogProperty = byKeyProperty.Count,
            global = global?.MaterialId,
            globalProperty = globalProperty?.MaterialId,
            controlFace = controlFace?.MaterialId,
            layers = global?.Layers.Count ?? 0,
            unresolvedTokens = SkinDiagnostics.Current.UnresolvedTokenCount,
            borderStrokes = border?.Spec.Strokes.Count ?? 0,
            borderIssues = border?.Issues.Count ?? 0,
            borderIssueCodes = border is null
                ? string.Empty
                : string.Join(",", border.Issues.Select(issue => issue.Code).Distinct()),
            ambient = ambient.HasScene,
            ambientScene = ambient.HasScene,
            engineEnabled = SurfaceFramePump.Enabled,
            engineQuality = SurfaceFramePump.Quality,
            pageBackground = pageBackground?.ToArgb(),
            ambientGutter = ambient.Gutter,
            ambientRibbon = ambient.RibbonWidth,
            decorUndeclared,
        });
        return SkinDecorator.Enabled;
    }

    /// <summary>
    /// 页面底色 = 默认档 <c>panel_surface</c> 槽的首个不透明 fill 层值。
    /// <para>只用声明推导（禁代码硬编码色值）：默认档 <c>panel_surface → flat → base=page_fill</c>。</para>
    /// <para>结构固定按 <see cref="SurfaceResolver.DefaultProfile"/>，颜色按当前档位的令牌取值（浅/深各一值）。</para>
    /// <para>未声明 / 未换值 token / 非不透明 ⇒ null（宿主回落系统底色，零回归）。</para>
    /// </summary>
    private static Color? ResolvePageColor(
        SurfaceProgramDeclaration program,
        SurfaceResolveContext context,
        string carrierId,
        IReadOnlyDictionary<string, string> tokens)
    {
        foreach (var view in program.Views.Keys)
        {
            var resolved = SurfaceResolver.Resolve(
                program, context, view, carrierId, SurfaceResolver.DefaultProfile,
                tokens: tokens);
            foreach (var container in resolved.Containers)
            {
                foreach (var material in container.Materials.Values)
                {
                    // 取值口径单源 = SurfaceMaterialFace（fill|gradient、A>0 叠到系统面）：
                    // 与属性通道/宿主合成器同一判据，避免「有材质却无页底色」回落成系统灰。
                    if (SurfaceMaterialFace.TryGetOpaqueFill(material, SystemColors.Control.ToArgb(), out var argb))
                        return Color.FromArgb(argb);
                }
            }
        }

        return null;
    }

    /// <summary>全局回退材质（唯一实现：<see cref="SurfaceSlotSelection.SelectGlobal"/>）。</summary>
    private static ResolvedMaterial? SelectGlobal(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, ResolvedMaterial>> byKey, string slot)
        => SurfaceSlotSelection.SelectGlobal(byKey, slot);

    /// <summary>卸载外观（清目录 + 清全局材质 + 全量 Detach）。</summary>
    public static void Uninstall()
    {
        SkinMaterialCatalog.Set(null);
        SkinMaterialCatalog.SetProperty(null);
        SkinMaterialCatalog.SetGlobal(null);
        SkinMaterialCatalog.SetGlobalProperty(null);
        SkinMaterialCatalog.SetFamilyProperty(null);
        SkinMaterialCatalog.SetFamilyDraw(null);
        SkinMaterialCatalog.SetPageBackground(null);
        SurfaceAmbient.Reset();
        SkinDecorator.Disable();
        SkinDiagnostics.RecordUninstall();
        // E-21：卸载即清宿主事实，避免测试/重装配串状态。
        SurfaceHostFacts.Reset();
    }
}
