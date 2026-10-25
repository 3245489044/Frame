#nullable enable

using System;
using FrontEndDriven.Pipeline;
using Share.StateManagement;

namespace FrontEndDriven.Gate;

/// <summary>
/// 作用域销毁单一出口（通用收敛）——「有 StateCenter 生命周期 → 走 ScopeDestroyed；否则回退 composition 直毁」
/// 的回退分支此前在 EcsGate / RenderTransaction / ScopeLifecycleAdapter 各复制一份，补逻辑（事件发布/挂载态回写）必漏其一。
/// 现收敛为唯一实现：所有销毁路径经此调用。
/// </summary>
public static class CompositionTeardown
{
    /// <summary>经生命周期出口销毁（StateCenter 单一路径；绝不 Dispose composition）。</summary>
    public static void DestroyScope(ICompositionLifecycle lifecycle, string scope)
    {
        ArgumentNullException.ThrowIfNull(lifecycle);
        lifecycle.ScopeDestroyed(scope);
    }

    /// <summary>生命周期缺省时回退到 composition 直毁（无 StateCenter 装配场景）。</summary>
    public static void DestroyScope(
        ICompositionLifecycle? lifecycle,
        RenderPipelineComposition composition,
        string scope)
    {
        ArgumentNullException.ThrowIfNull(composition);
        if (lifecycle != null)
            lifecycle.ScopeDestroyed(scope);
        else
            composition.DestroyScopeEntities(scope);
    }
}