namespace Operation.HierarchyMethod.History.Runtime;

/// <summary>
/// 单 task 运行控制/状态读取端口（由 Agent 应用层注入适配器实现）。
/// History 编排层（<see cref="HistoryRuntimeService"/>）只做「按 history 作用域批量展开」的编排；
/// 单 task 的状态读取与启动/暂停/恢复/取消/步进原语经本端口委托到任务池/会话管线既有实现
/// （SingleImplementation / TH-12，不复制推进与控制逻辑）。
/// 装配可空：未注入 → HistoryRuntimeService 返回空视图 / 控制计数 0（对齐「未装配返回诊断」模式）。
/// </summary>
public interface IHistoryRuntimePort
{
    /// <summary>读取单 task 运行状态；未命中/未装配返回 null。</summary>
    Task<HistoryTaskRunStatus?> GetStatusAsync(string taskKey, CancellationToken ct = default);

    /// <summary>按装配声明启动单 task（写即激活消费）；返回是否已触发。</summary>
    Task<bool> StartAsync(string taskKey, HistoryPromptProfile? profile, string? input, CancellationToken ct = default);

    /// <summary>暂停单 task（Running⇄Paused 非终止）。</summary>
    Task<bool> PauseAsync(string taskKey, CancellationToken ct = default);

    /// <summary>恢复单 task。</summary>
    Task<bool> ResumeAsync(string taskKey, CancellationToken ct = default);

    /// <summary>取消单 task（终止域，Cancelled）。</summary>
    Task<bool> CancelAsync(string taskKey, CancellationToken ct = default);

    /// <summary>单 task 步进一轮（新消息单轮 send，或对运行中任务续跑）。</summary>
    Task<bool> StepAsync(string taskKey, string? input, CancellationToken ct = default);
}
