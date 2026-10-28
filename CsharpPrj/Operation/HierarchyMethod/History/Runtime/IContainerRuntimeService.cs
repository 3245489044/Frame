namespace Operation.HierarchyMethod.History.Runtime;

/// <summary>
/// history 会话编排统一门面（运行态聚合 + 批量控制 + 装配声明）。
/// 三大原则（对齐 .clinerules）：
///   · **只读投影**：运行态视图经 <see cref="IHistoryRuntimePort"/> 读取聚合，非独立状态源（StateRootSingleSource）；
///   · **只编排不复制**：控制按 history_owns_task 作用域批量展开，单 task 原语委托 <see cref="IHistoryRuntimePort"/>（SingleImplementation / TH-12）；
///   · **装配声明**：启动装配经 <see cref="HistoryPromptProfile"/> 承载，读写委托注入的 profile store（可空回退内存）。
/// 装配可空：未装配关系端口 → 视图空集 / 控制 0；未装配运行端口 → 控制 no-op（对齐「未装配返回诊断」模式）。
/// </summary>
public interface IHistoryRuntimeService
{
    /// <summary>聚合该 history 归拢任务的运行态视图（一次总览多 task）。</summary>
    Task<HistoryRuntimeView> GetOverviewAsync(string historyId, CancellationToken ct = default);

    /// <summary>按装配声明批量启动；<paramref name="taskKeys"/> 空 = 该 history 全部已归拢任务。返回实际启动数。</summary>
    Task<int> StartAsync(string historyId, HistoryPromptProfile profile, string? input = null, IReadOnlyList<string>? taskKeys = null, CancellationToken ct = default);

    /// <summary>暂停；<paramref name="taskKey"/> 空 = 全部在途任务。返回实际暂停数。</summary>
    Task<int> PauseAsync(string historyId, string? taskKey = null, CancellationToken ct = default);

    /// <summary>恢复；<paramref name="taskKey"/> 空 = 全部已暂停任务。返回实际恢复数。</summary>
    Task<int> ResumeAsync(string historyId, string? taskKey = null, CancellationToken ct = default);

    /// <summary>取消；<paramref name="taskKey"/> 空 = 全部任务。返回实际取消数。</summary>
    Task<int> CancelAsync(string historyId, string? taskKey = null, CancellationToken ct = default);

    /// <summary>单 task 步进一轮（作用域守卫：目标必须归拢于该 history）。返回是否已执行。</summary>
    Task<bool> StepAsync(string historyId, string taskKey, string? input = null, CancellationToken ct = default);

    /// <summary>持久化该 history 的装配声明（未注入 store → 内存回退）。</summary>
    Task SetProfileAsync(string historyId, HistoryPromptProfile profile, CancellationToken ct = default);

    /// <summary>读取该 history 的装配声明；未设置/未装配 → null。</summary>
    Task<HistoryPromptProfile?> GetProfileAsync(string historyId, CancellationToken ct = default);
}
