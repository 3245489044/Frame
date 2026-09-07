using System;
using System.Collections.Generic;
using Operation.LoopFoldMethod.Contracts;

namespace Operation.LoopFoldMethod.Routing;

/// <summary>
/// 作用点路由表 —— 宿主面向路由层的装配器（RouteContext 语义）：把各作用点的 Router / Handler 按固定阶段登记，
/// 产出 <c>SpinalPipeline</c> 所需的 stage→router 映射。自由逻辑只经此处登记进入，语义不进固定层。
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
public sealed class RouteTable<TState>
{
    private readonly Dictionary<ActionStageId, IActionRouter<TState>> _routers = new();

    /// <summary>登记某固定作用点的 Router（同名重复登记 fail-fast：防第二套路由漂移，TH-12）。</summary>
    public RouteTable<TState> At(ActionStageId stage, IActionRouter<TState> router)
    {
        ArgumentNullException.ThrowIfNull(router);
        if (!_routers.TryAdd(stage, router))
            throw new InvalidOperationException($"作用点 {stage} 已登记 Router，禁止二次登记（单一路由权威）。");
        return this;
    }

    /// <summary>登记某固定作用点的单 handler（提升为恒返回该 handler 的最小路由）。</summary>
    public RouteTable<TState> At(ActionStageId stage, IActionHandler<TState> handler)
        => At(stage, new SingleHandlerRouter<TState>(handler));

    /// <summary>产出只读 stage→router 映射（供 <c>SpinalPipeline</c> 消费）。</summary>
    public IReadOnlyDictionary<ActionStageId, IActionRouter<TState>> Build() => _routers;
}

/// <summary>恒返回单 handler 的路由（把单 handler 提升为 Router 的最小适配）。</summary>
internal sealed class SingleHandlerRouter<TState> : IActionRouter<TState>
{
    private readonly IActionHandler<TState> _handler;
    public SingleHandlerRouter(IActionHandler<TState> handler) => _handler = handler;
    public IActionHandler<TState>? Select(ActionCall<TState> call) => _handler;
}
