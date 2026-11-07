#nullable enable

using System;
using System.Collections.Generic;
using System.Drawing;
using Share.Conversation.Rendering.Surface;

namespace FrontEndDriven.WinForms.Paint;

/// <summary>
/// 材质目录（进程级·单一写入点）：容器键 → 槽 → 已解析材质。
/// <para>宿主持有「表面声明 + 当前档」，在装配/换肤时推入；控件侧只按 <c>descriptor.Id/Name</c> 取。</para>
/// <para>未命中 ⇒ null（禁静默造材质）：材质缺失必须回落到纯属性着色，且可见化。</para>
/// </summary>
public static class SkinMaterialCatalog
{
    private static readonly IReadOnlyDictionary<string, IReadOnlyDictionary<string, ResolvedMaterial>> EmptyCatalog =
        new Dictionary<string, IReadOnlyDictionary<string, ResolvedMaterial>>(StringComparer.Ordinal);

    private static readonly IReadOnlyDictionary<string, ResolvedMaterial> EmptySlots =
        new Dictionary<string, ResolvedMaterial>(StringComparer.Ordinal);

    private static volatile IReadOnlyDictionary<string, IReadOnlyDictionary<string, ResolvedMaterial>> _byKey = EmptyCatalog;
    private static volatile IReadOnlyDictionary<string, IReadOnlyDictionary<string, ResolvedMaterial>> _byKeyProperty = EmptyCatalog;
    private static volatile IReadOnlyDictionary<string, ResolvedMaterial> _familyProperty = EmptySlots;
    private static volatile IReadOnlyDictionary<string, ResolvedMaterial> _familyDraw = EmptySlots;

    /// <summary>整体替换目录（null ⇒ 清空；换肤/失效的**唯一写入口**）。</summary>
    /// <param name="byKey">容器键 → 槽 → 材质（draw 通道）。</param>
    public static void Set(IReadOnlyDictionary<string, IReadOnlyDictionary<string, ResolvedMaterial>>? byKey)
        => _byKey = byKey ?? EmptyCatalog;

    /// <summary>整体替换属性通道目录（null ⇒ 清空；唯一写入口）。</summary>
    /// <param name="byKey">容器键 → 槽 → 材质（property 通道，已按能力降级）。</param>
    public static void SetProperty(IReadOnlyDictionary<string, IReadOnlyDictionary<string, ResolvedMaterial>>? byKey)
        => _byKeyProperty = byKey ?? EmptyCatalog;

    /// <summary>整体替换族级属性通道目录（槽 → 材质；唯一写入口）。</summary>
    public static void SetFamilyProperty(IReadOnlyDictionary<string, ResolvedMaterial>? bySlot)
        => _familyProperty = bySlot ?? EmptySlots;

    /// <summary>整体替换族级绘制通道目录（槽 → 材质；唯一写入口；只放结构宿主等跨容器槽）。</summary>
    public static void SetFamilyDraw(IReadOnlyDictionary<string, ResolvedMaterial>? bySlot)
        => _familyDraw = bySlot ?? EmptySlots;

    /// <summary>按材质槽取族级属性通道材质（未命中 ⇒ null）。</summary>
    public static ResolvedMaterial? FamilyProperty(string? slot)
        => slot is { Length: > 0 } && _familyProperty.TryGetValue(slot, out var material) ? material : null;

    /// <summary>按材质槽取族级绘制通道材质（未命中 ⇒ null）。</summary>
    public static ResolvedMaterial? FamilyDraw(string? slot)
        => slot is { Length: > 0 } && _familyDraw.TryGetValue(slot, out var material) ? material : null;

    /// <summary>清空目录。</summary>
    public static void Clear()
    {
        Set(null);
        SetProperty(null);
        SetGlobal(null);
        SetGlobalProperty(null);
        SetFamilyProperty(null);
        SetFamilyDraw(null);
        SetPageBackground(null);
    }

    /// <summary>
    /// 页面底色（**未上色背后的唯一兜底**）：玻璃快照与属性通道遇到「系统默认底色」时用它替代
    /// <see cref="SystemColors.Control"/>，否则面板会被浅灰底洗白。null ⇒ 保持系统色（声明缺失零回归）。
    /// </summary>
    public static Color? PageBackground { get; private set; }

    /// <summary>设置页面底色（唯一写入口）。</summary>
    /// <param name="color">页面底色（null ⇒ 清除）。</param>
    public static void SetPageBackground(Color? color) => PageBackground = color;

    /// <summary>全局回退材质（绘制通道；null ⇒ 无回退，保持纯属性着色）。</summary>
    public static ResolvedMaterial? Global { get; private set; }

    /// <summary>全局回退材质（属性通道；已按通道能力降级）。</summary>
    public static ResolvedMaterial? GlobalProperty { get; private set; }

    /// <summary>设置全局回退材质（唯一写入口；与主机无关）。</summary>
    /// <param name="material">已解析材质（null ⇒ 清除）。</param>
    public static void SetGlobal(ResolvedMaterial? material) => Global = material;

    /// <summary>设置属性通道全局回退材质（唯一写入口）。</summary>
    /// <param name="material">已解析材质（null ⇒ 清除）。</param>
    public static void SetGlobalProperty(ResolvedMaterial? material) => GlobalProperty = material;

    /// <summary>按容器键取材质（绘制通道；未命中 ⇒ null）。</summary>
    /// <param name="key">容器键（控件 Id 或容器名）。</param>
    public static IReadOnlyDictionary<string, ResolvedMaterial>? For(string? key)
        => key is { Length: > 0 } && _byKey.TryGetValue(key, out var materials) ? materials : null;

    /// <summary>按容器键取材质（属性通道；未命中 ⇒ null）。</summary>
    /// <param name="key">容器键（控件 Id 或容器名）。</param>
    public static IReadOnlyDictionary<string, ResolvedMaterial>? ForProperty(string? key)
        => key is { Length: > 0 } && _byKeyProperty.TryGetValue(key, out var materials) ? materials : null;

    /// <summary>当前目录快照（只读；诊断/断言用，禁写入）。</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, ResolvedMaterial>> Snapshot => _byKey;

    /// <summary>属性通道目录快照（只读；诊断/断言用）。</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, ResolvedMaterial>> SnapshotProperty => _byKeyProperty;

    /// <summary>目录条目数（诊断/测试判据）。</summary>
    public static int Count => _byKey.Count;
}
