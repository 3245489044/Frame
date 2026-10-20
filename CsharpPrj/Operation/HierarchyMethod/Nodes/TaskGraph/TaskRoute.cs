namespace Operation.HierarchyMethod.Nodes.TaskGraph
{
    /// <summary>
    /// 任务路由解析结果（唯一类型）：<see cref="Ok"/>=false 时 <see cref="Code"/> 为结构化诊断码，
    /// 调用方不得建任务、不得写半成品（判据载体 = 标签是否登记 / 执行腿是否为空）。
    /// </summary>
    public readonly record struct TaskRoute(bool Ok, string? ExecutorId, string? Code)
    {
        /// <summary>放行（executorId 可空 = 沿用默认路由）。</summary>
        public static TaskRoute Allow(string? executorId) => new(true, executorId, null);

        /// <summary>拒绝（code = 结构事实诊断码，如 subagent_tag_unknown:&lt;tag&gt;）。</summary>
        public static TaskRoute Reject(string code) => new(false, null, code);
    }

    /// <summary>
    /// 「子智能体标签 / 显式执行腿 → 执行腿」解析端口（唯一实现点）。
    /// Frame 定义、Agent.Core 实现；写图服务、成员供给、UI 入队共用同一实例（SingleImplementation）。
    /// </summary>
    public interface ITaskRouteResolver
    {
        TaskRoute Resolve(string? subAgentTag, string? executorId);
    }
}
