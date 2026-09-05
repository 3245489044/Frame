namespace Share.TaskStatusPool.Routing;

/// <summary>
/// 任务自主标记 —— 声明"该任务是否需要 LLM（自主生成 / 工具）/ 可否零 LLM 确定性处理"。
/// 由任务类型实现，或经适配器注入；结合"后台规则驱动"共同决定任务执行走向（见 <see cref="TaskRouter"/>）。
/// 通用任务路由 canonical，不依赖任何具体任务类型。
/// </summary>
public interface ITaskAutonomy
{
    /// <summary>是否需要 LLM（自主）。false = 可零 LLM（后台规则驱动 / 纯确定性）。</summary>
    bool RequiresLlm { get; }
}
