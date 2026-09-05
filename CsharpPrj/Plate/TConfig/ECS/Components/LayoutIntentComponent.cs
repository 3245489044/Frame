
using TConfig.ECS;

namespace TConfig.ECS.Components;

/// <summary>
/// 布局意图组件（M2·布局意图化）：dock/anchor 意图由 RenderPort 映射原生 Dock/Anchor（不再自算像素），
/// fixed 由 PixelLayoutComponent 承载；仅 proportional 走 ProportionalLayoutComponent 比例计算。
/// </summary>
public struct LayoutIntentComponent : IComponent
{
    /// <summary>"dock" / "anchor"（"fixed"/"proportional" 由对应组件承载，不写本组件）。</summary>
    public string Intent;

    /// <summary>原生 Dock 枚举名（Fill/Top/Bottom/Left/Right/None），Intent="dock" 时生效。</summary>
    public string Dock;

    /// <summary>原生 Anchor 组合（如 "Left,Right,Top,Bottom"），Intent="anchor" 时生效。</summary>
    public string Anchor;
}
