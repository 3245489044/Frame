#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using FrontEndDriven.Diagnostics;
using Share.Conversation.Rendering.Surface;
using UiBackend;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 外观安装器（**宿主无关的唯一装配入口**）：吃表面声明 → 解析材质目录 + 全局回退材质 → 点亮 owner-draw。
/// <para>宿主只需一行：<c>SkinAppearanceInstaller.Install(SurfaceProgramDeclaration.Parse(json))</c>。</para>
/// <para>声明不可用 ⇒ 卸载并回到纯属性着色（零回归）；档位来自声明 <c>active_profile</c>，禁代码硬编码。</para>
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
    /// <param name="profileId">外观档（null/空 ⇒ 声明 <c>active_profile</c>）。</param>
    /// <param name="carrierId">载体 id（WinForms = <c>winforms</c>）。</param>
    /// <returns>是否已启用 owner-draw。</returns>
    public static bool Install(
        SurfaceProgramDeclaration program,
        string? profileId = null,
        string carrierId = "winforms")
    {
        if (program is null || !program.IsDeclared)
        {
            Uninstall();
            return false;
        }

        var profile = string.IsNullOrWhiteSpace(profileId) ? program.ActiveProfile : profileId;
        var byKey = new Dictionary<string, IReadOnlyDictionary<string, ResolvedMaterial>>(StringComparer.Ordinal);
        var byKeyProperty = new Dictionary<string, IReadOnlyDictionary<string, ResolvedMaterial>>(StringComparer.Ordinal);

        // 能力矩阵/层规格只建一次：每视图两次解析（draw / property）共用同一上下文。
        var context = SurfaceResolver.Prepare(program);
        var pageBackground = ResolvePageColor(program, context, carrierId, profile);
        foreach (var view in program.Views.Keys)
        {
            var resolved = SurfaceResolver.Resolve(program, context, view, carrierId, profile);
            var resolvedProperty = SurfaceResolver.Resolve(
                program, context, view, carrierId, profile,
                channel: WinFormsPropertyProjector.ChannelId);
            foreach (var container in resolved.Containers)
            {
                if (container.Materials.Count == 0) continue;
                byKey[container.Id] = container.Materials;
                byKey.TryAdd(container.Key, container.Materials);
            }

            foreach (var container in resolvedProperty.Containers)
            {
                if (container.Materials.Count == 0) continue;
                byKeyProperty[container.Id] = container.Materials;
                byKeyProperty.TryAdd(container.Key, container.Materials);
            }
        }

        SkinMaterialCatalog.SetPageBackground(pageBackground);
        // 页面装饰层：唯一取值来源 = 当前档令牌（浅档主/深档备），代码端零颜色分支。
        var ambient = SurfaceAmbientParams.FromTokens(SurfaceResolver.TokensForProfile(program, profile));
        SurfaceAmbient.Configure(ambient);
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

        SkinMaterialCatalog.Set(byKey);
        SkinMaterialCatalog.SetProperty(byKeyProperty);
        SkinMaterialCatalog.SetFamilyProperty(familyProperty);
        SkinMaterialCatalog.SetFamilyDraw(familyDraw);
        SkinMaterialCatalog.SetGlobal(global);
        SkinMaterialCatalog.SetGlobalProperty(globalProperty);
        SkinDecorator.Enabled = global is not null || globalProperty is not null;
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
            ambientGutter = ambient.Gutter,
            ambientRibbon = ambient.RibbonWidth,
            decorMaterials = string.Join(",", decorMaterials),
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
        string profile)
    {
        foreach (var view in program.Views.Keys)
        {
            var resolved = SurfaceResolver.Resolve(
                program, context, view, carrierId, SurfaceResolver.DefaultProfile,
                tokens: SurfaceResolver.TokensForProfile(program, profile));
            foreach (var container in resolved.Containers)
            {
                foreach (var material in container.Materials.Values)
                {
                    foreach (var layer in material.Layers)
                    {
                        if (!string.Equals(layer.Primitive, "fill", StringComparison.Ordinal)) continue;
                        if (!layer.Params.TryGetValue("value", out var value) || value.Length == 0) continue;
                        if (!SurfaceColor.TryParseColor(value, out var argb)) continue;
                        if ((argb & unchecked((int)0xFF000000)) != unchecked((int)0xFF000000)) continue;
                        return Color.FromArgb(argb);
                    }
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
    }
}
