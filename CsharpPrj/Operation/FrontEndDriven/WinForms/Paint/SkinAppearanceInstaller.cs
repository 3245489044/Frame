#nullable enable

using System;
using System.Collections.Generic;
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
        ResolvedMaterial? global = null;

        foreach (var view in program.Views.Keys)
        {
            var resolved = SurfaceResolver.Resolve(program, view, carrierId, profile);
            foreach (var container in resolved.Containers)
            {
                if (container.Materials.Count == 0) continue;
                byKey[container.Id] = container.Materials;
                byKey.TryAdd(container.Key, container.Materials);
                if (global is null
                    && container.Materials.TryGetValue("panel_surface", out var surfaceMaterial)
                    && surfaceMaterial.Layers.Count > 0)
                {
                    global = surfaceMaterial;
                }
            }
        }

        // 无 panel_surface 槽时退回「第一个有层栈的材质」（声明序），仍无 ⇒ 不启用。
        if (global is null)
        {
            foreach (var materials in byKey.Values)
            {
                foreach (var candidate in materials.Values)
                {
                    if (candidate.Layers.Count == 0) continue;
                    global = candidate;
                    break;
                }

                if (global is not null) break;
            }
        }

        SkinMaterialCatalog.Set(byKey);
        SkinMaterialCatalog.SetGlobal(global);
        SkinDecorator.Enabled = global is not null;
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
            global = global?.MaterialId,
            layers = global?.Layers.Count ?? 0,
            unresolvedTokens = SkinDiagnostics.Current.UnresolvedTokenCount,
        });
        return SkinDecorator.Enabled;
    }

    /// <summary>卸载外观（清目录 + 清全局材质 + 全量 Detach）。</summary>
    public static void Uninstall()
    {
        SkinMaterialCatalog.Set(null);
        SkinMaterialCatalog.SetGlobal(null);
        SkinDecorator.Disable();
        SkinDiagnostics.RecordUninstall();
    }
}
