namespace Share.TaskStatusPool.Routing;

/// <summary>
/// 后台规则驱动端口 —— 零 LLM 确定性处理的任务能否 / 如何被规则满足。
/// 由消费方注入具体实现（如 ur_config 规则集评估）；本层仅承载路由判定逻辑，
/// 不承载 IO / 规则存储 / 任务池接线。
/// </summary>
/// <typeparam name="TItem">任务类型（泛型，不依赖具体任务类型）。</typeparam>
public interface IRuleBackend<TItem>
{
    /// <summary>给定任务，返回命中的确定性规则动作；null = 无规则命中 / 不可零 LLM 处理。</summary>
    RuleResolution? TryResolve(TItem item);
}
