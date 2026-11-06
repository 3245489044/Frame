#nullable enable

using System;
using System.Collections.Generic;
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

    private static volatile IReadOnlyDictionary<string, IReadOnlyDictionary<string, ResolvedMaterial>> _byKey = EmptyCatalog;

    /// <summary>整体替换目录（null ⇒ 清空；换肤/失效的**唯一写入口**）。</summary>
    /// <param name="byKey">容器键 → 槽 → 材质。</param>
    public static void Set(IReadOnlyDictionary<string, IReadOnlyDictionary<string, ResolvedMaterial>>? byKey)
        => _byKey = byKey ?? EmptyCatalog;

    /// <summary>清空目录。</summary>
    public static void Clear() => Set(null);

    /// <summary>全局回退材质（控件未命中容器键时使用；null ⇒ 无回退，保持纯属性着色）。</summary>
    public static ResolvedMaterial? Global { get; private set; }

    /// <summary>设置全局回退材质（唯一写入口；与主机无关）。</summary>
    /// <param name="material">已解析材质（null ⇒ 清除）。</param>
    public static void SetGlobal(ResolvedMaterial? material) => Global = material;

    /// <summary>按容器键取材质（未命中 ⇒ null）。</summary>
    /// <param name="key">容器键（控件 Id 或容器名）。</param>
    public static IReadOnlyDictionary<string, ResolvedMaterial>? For(string? key)
        => key is { Length: > 0 } && _byKey.TryGetValue(key, out var materials) ? materials : null;

    /// <summary>当前目录快照（只读；诊断/断言用，禁写入）。</summary>
    public static IReadOnlyDictionary<string, IReadOnlyDictionary<string, ResolvedMaterial>> Snapshot => _byKey;

    /// <summary>目录条目数（诊断/测试判据）。</summary>
    public static int Count => _byKey.Count;
}
