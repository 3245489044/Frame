#nullable enable

using System;
using System.Collections.Generic;

namespace Share.Conversation.Rendering.Surface;

/// <summary>
/// <c>surface-decor</c> 的域产物（**装饰计划唯一来源**）：卡片键 → 装饰计划 + 角色分层。
/// </summary>
/// <param name="Entries">卡片键 → 条目。</param>
public sealed record SurfaceDecorBundle(IReadOnlyDictionary<string, SurfaceDecorEntry> Entries)
{
    /// <summary>空产物（无卡片 ⇒ 不画面，不是失败）。</summary>
    public static readonly SurfaceDecorBundle Empty =
        new(new Dictionary<string, SurfaceDecorEntry>(StringComparer.Ordinal));

    /// <summary>按卡片键取条目（未命中 ⇒ null）。</summary>
    /// <param name="cardKey">卡片稳定键。</param>
    public SurfaceDecorEntry? For(string cardKey)
        => Entries.TryGetValue(cardKey, out var entry) ? entry : null;
}

/// <summary>
/// 一张卡片的装饰条目：角色/层级/深度 + 唯一装饰计划 + 材质槽。
/// <para><see cref="MissingDeclaration"/> 为 true ⇒ 声明缺失（必须记账 <c>decor-undeclared</c>，禁静默按默认画）。</para>
/// </summary>
/// <param name="CardKey">卡片键（与几何同源）。</param>
/// <param name="Role">角色（host | card | nested | leaf）。</param>
/// <param name="Tier">层级（root | nested）。</param>
/// <param name="Depth">父链深度。</param>
/// <param name="Plan">装饰计划（唯一实现 = <see cref="SurfaceDecorPlan.Resolve"/>）。</param>
/// <param name="MaterialSlot">材质槽。</param>
/// <param name="MissingDeclaration">声明是否缺失。</param>
public sealed record SurfaceDecorEntry(
    string CardKey,
    string Role,
    string Tier,
    int Depth,
    SurfaceDecorPlan Plan,
    string MaterialSlot,
    bool MissingDeclaration)
{
    /// <summary>是否继承父面（不产面）。</summary>
    public bool Inherits => !string.Equals(Tier, "root", StringComparison.Ordinal) && Plan.Stages.Count == 0;

    /// <summary>缺失声明条目（显式降级载体）。</summary>
    /// <param name="cardKey">卡片键。</param>
    /// <param name="slot">材质槽。</param>
    public static SurfaceDecorEntry Missing(string cardKey, string slot)
        => new(cardKey, "face", "root", 0, SurfaceDecorPlan.None, slot, true);
}
