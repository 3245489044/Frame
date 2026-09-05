#nullable enable

using System;
using ECS.Core;
using FrameworkAbstractions.ComponentRegistry;
using FrameworkAbstractions.WinFormsAbstractions;

namespace FrontEndDriven.ControlLookup;

/// <summary>
/// 按控件名解析当前有效 Entity 的共享助手（D1 收敛）：
/// 作用域感知解析（<see cref="ScopeAwareResolver"/>）+ 未命中回退 <see cref="Entity.Invalid"/>。
/// 消除 EcsEventBus / EcsStateAccessor 中同构 FindEntityByName 复制（原本各自手写
/// 「Resolve IScopeContext → ScopeAwareResolver.ResolveEntity」三份）。
/// </summary>
public static class EntityLookup
{
    /// <summary>作用域感知按名解析；未命中返回 <see cref="Entity.Invalid"/>。</summary>
    public static Entity FindByName(EntityQueryService query, string controlName)
    {
        var ctx = AppServiceProvider.Resolve<IScopeContext>();
        return ScopeAwareResolver.ResolveEntity(query, ctx, controlName) ?? Entity.Invalid;
    }
}
