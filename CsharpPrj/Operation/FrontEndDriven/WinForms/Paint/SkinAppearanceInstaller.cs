#nullable enable

using System;
using System.Collections.Generic;
using System.Linq;
using FrontEndDriven.Diagnostics;
using Share.Conversation.Rendering.Surface;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 外观安装器（**宿主无关的唯一装配入口**）：吃表面声明 → 解析材质目录 + 全局回退材质 → 点亮 owner-draw。
/// <para>宿主只需一行：<c>SkinAppearanceInstaller.Install(SurfaceProgramDeclaration.Parse(json))</c>。</para>
/// <para>声明不可用 ⇒ 卸载并回到纯属性着色（零回归）；档位来自声明 <c>active_profile</c>，禁代码硬编码。</para>
/// </summary>
public static class SkinAppearanceInstaller
{
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

        var global = SelectGlobal(byKey, "panel_surface");
        var globalProperty = SelectGlobal(byKeyProperty, "panel_surface");

        SkinMaterialCatalog.Set(byKey);
        SkinMaterialCatalog.SetProperty(byKeyProperty);
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
            layers = global?.Layers.Count ?? 0,
            unresolvedTokens = SkinDiagnostics.Current.UnresolvedTokenCount,
            borderStrokes = border?.Spec.Strokes.Count ?? 0,
            borderIssues = border?.Issues.Count ?? 0,
            borderIssueCodes = border is null
                ? string.Empty
                : string.Join(",", border.Issues.Select(issue => issue.Code).Distinct()),
        });
        return SkinDecorator.Enabled;
    }

    /// <summary>全局回退材质（唯一实现：<see cref="SurfaceSlotSelection.SelectGlobal"/>）。</summary>
    private static ResolvedMaterial? SelectGlobal(
        IReadOnlyDictionary<string, IReadOnlyDictionary<string, ResolvedMaterial>> byKey, string slot)
        => SurfaceSlotSelection.SelectGlobal(byKey, slot);

    /// <summary>卸载外观（清目录 + 清全局材质 + 全量 Detach）。</summary>
    public static void Uninstall()
    {
        SkinMaterialCatalog.Set(null);
        SkinMaterialCatalog.SetGlobal(null);
        SkinDecorator.Disable();
        SkinDiagnostics.RecordUninstall();
    }
}
