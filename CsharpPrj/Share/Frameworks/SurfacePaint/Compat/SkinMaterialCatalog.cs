#nullable enable

using System.Collections.Generic;
using System.Drawing;
using Share.Conversation.Rendering.Surface;
using Share.SurfacePaint.Materials;

namespace Share.SurfacePaint.Compat;

/// <summary>
/// 材质目录（宿主兼容门面）：实现单源 = <see cref="SurfaceMaterialCatalog"/>。
/// </summary>
public static class SkinMaterialCatalog
{
    /// <summary>整体替换目录（null ⇒ 清空）。</summary>
    public static void Set(IReadOnlyDictionary<string, IReadOnlyDictionary<string, ResolvedMaterial>>? byKey)
        => SurfaceMaterialCatalog.Set(byKey);

    /// <summary>整体替换属性通道目录（null ⇒ 清空）。</summary>
    public static void SetProperty(IReadOnlyDictionary<string, IReadOnlyDictionary<string, ResolvedMaterial>>? byKey)
        => SurfaceMaterialCatalog.SetProperty(byKey);

    /// <summary>整体替换族级属性通道目录。</summary>
    public static void SetFamilyProperty(IReadOnlyDictionary<string, ResolvedMaterial>? bySlot)
        => SurfaceMaterialCatalog.SetFamilyProperty(bySlot);

    /// <summary>整体替换族级绘制通道目录。</summary>
    public static void SetFamilyDraw(IReadOnlyDictionary<string, ResolvedMaterial>? bySlot)
        => SurfaceMaterialCatalog.SetFamilyDraw(bySlot);

    /// <summary>按材质槽取族级属性通道材质。</summary>
    public static ResolvedMaterial? FamilyProperty(string? slot) => SurfaceMaterialCatalog.FamilyProperty(slot);

    /// <summary>按材质槽取族级绘制通道材质。</summary>
    public static ResolvedMaterial? FamilyDraw(string? slot) => SurfaceMaterialCatalog.FamilyDraw(slot);

    /// <summary>清空目录。</summary>
    public static void Clear() => SurfaceMaterialCatalog.Clear();

    /// <summary>页面底色。</summary>
    public static Color? PageBackground => SurfaceMaterialCatalog.PageBackground;

    /// <summary>设置页面底色。</summary>
    public static void SetPageBackground(Color? color) => SurfaceMaterialCatalog.SetPageBackground(color);

    /// <summary>全局回退材质（绘制通道）。</summary>
    public static ResolvedMaterial? Global => SurfaceMaterialCatalog.Global;

    /// <summary>全局回退材质（属性通道）。</summary>
    public static ResolvedMaterial? GlobalProperty => SurfaceMaterialCatalog.GlobalProperty;

    /// <summary>设置全局回退材质。</summary>
    public static void SetGlobal(ResolvedMaterial? material) => SurfaceMaterialCatalog.SetGlobal(material);

    /// <summary>设置属性通道全局回退材质。</summary>
    public static void SetGlobalProperty(ResolvedMaterial? material) => SurfaceMaterialCatalog.SetGlobalProperty(material);

    /// <summary>按容器键取材质（绘制通道）。</summary>
    public static IReadOnlyDictionary<string, ResolvedMaterial>? For(string? key) => SurfaceMaterialCatalog.For(key);

    /// <summary>按容器键取材质（属性通道）。</summary>
    public static IReadOnlyDictionary<string, ResolvedMaterial>? ForProperty(string? key)
        => SurfaceMaterialCatalog.ForProperty(key);

    /// <summary>当前目录快照。</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, ResolvedMaterial>> Snapshot
        => SurfaceMaterialCatalog.Snapshot;

    /// <summary>属性通道目录快照。</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, ResolvedMaterial>> SnapshotProperty
        => SurfaceMaterialCatalog.SnapshotProperty;

    /// <summary>目录条目数。</summary>
    public static int Count => SurfaceMaterialCatalog.Count;
}
