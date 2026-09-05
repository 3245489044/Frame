using System;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>任务池运行配置（L3，运行时可变）。</summary>
public sealed class TaskPoolOptions
{
    /// <summary>并发处理上限。默认 1（串行）：当前引擎含全局静态取消/共享状态，并发 &gt;1 需宿主并发模型支撑。</summary>
    public int MaxConcurrency { get; set; } = 1;

    /// <summary>单轮从持久化拉取的待办批次上限。</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>入队→Drain→DB 查询触发频率限制窗口（毫秒）。默认 30000（30s）。防大量 enqueue 写风暴导致紧密 DB 查询；仅在有事件到达时门控（被动限频），非周期轮询。0 = 不限频（立即放行）。</summary>
    public int MinTriggerIntervalMs { get; set; } = 30_000;

    /// <summary>在途任务心跳刷新间隔（毫秒）。默认 15000（15s）。长期执行（如长 LLM 调用）期间周期性重写 Running 状态列刷新 UpdatedAt，使 stuck Running 可经更新时间陈旧度被外部观测；任务暂停/完成时条件更新（WHERE Status=Running）自然失效，不影响状态机。0 = 关闭心跳。</summary>
    public int HeartbeatIntervalMs { get; set; } = 15_000;

    /// <summary>按任务判定是否跳过触发频率限制门（true = 该任务不限频、立即放行；不更新限频窗口）。null = 不跳过（对所有任务统一限频）。用途：零 LLM / 本地任务即便真实环境也不受 30s 窗口限制；真实 LLM 任务仍限频。</summary>
    public Func<ITaskPoolItem, bool>? SkipRateLimitFor { get; set; }
}
