using Operation.LoopFoldMethod.Contracts;

namespace Operation.LoopFoldMethod.Routing;

/// <summary>
/// 作用点路由 —— 对某次作用点调用选择 handler（自由、可替换，只经作用点进入）。
/// 每级作用点挂一个 Router；返回 null = 该级未匹配 handler，由调用方决定跳过/兜底。
/// 本接口只承载「选谁」，选中的语义不回流固定层（窄接缝纪律）。
/// </summary>
/// <typeparam name="TState">状态点类型。</typeparam>
public interface IActionRouter<TState>
{
    /// <summary>为一次作用点调用选择 handler；null = 无匹配（可选作用点可返回 null 表示本步空转）。</summary>
    IActionHandler<TState>? Select(ActionCall<TState> call);
}
