namespace Share.TaskStatusPool.TaskPool;

/// <summary>任务池运行配置（L3，运行时可变）。</summary>
public sealed class TaskPoolOptions
{
    /// <summary>并发处理上限。默认 1（串行）：当前引擎含全局静态取消/共享状态，并发 &gt;1 需宿主并发模型支撑。</summary>
    public int MaxConcurrency { get; set; } = 1;

    /// <summary>单轮从持久化拉取的待办批次上限。</summary>
    public int BatchSize { get; set; } = 50;

    /// <summary>在途任务心跳刷新间隔（毫秒）。默认 15000（15s）。长期执行（如长 LLM 调用）期间周期性重写 Running 状态列刷新 UpdatedAt，使 stuck Running 可经更新时间陈旧度被外部观测；任务暂停/完成时条件更新（WHERE Status=Running）自然失效，不影响状态机。0 = 关闭心跳。</summary>
    public int HeartbeatIntervalMs { get; set; } = 15_000;

    /// <summary>单次 drain 内持久化 await 的有界等待超时（毫秒）。默认 0 = 不加超时（保持无界等待，向后兼容）；
    /// 显式配置（如对齐 9.1 语义设为 ≥5000）后，读侧卡死会在该时长后抛 TimeoutException 而非无限等待，由消费循环自愈续跑。</summary>
    public int DrainTimeoutMs { get; set; }

    /// <summary>消费循环低频终局兜底校验间隔（毫秒）。默认 0 = 关闭（纯事件驱动，向后兼容）。
    /// **唯一节流点**：事件驱动空闲等待的每个超周期唤醒一次补跑 drain；若存在「未被消费的 NotStarted 且 Active」条目
    /// （信号在某次该发而未发的路径丢失 → 睡死），即被纠偏消费；系统确无待办时该次 drain 立即空返回（ListAsync 空 → 无额外 DB 压力）。
    /// 与「写即醒拾」配合：前者从源头保证信号必发，本项兜底因 legacy 直写等历史残留行产生的睡死。建议仅在有"NotStarted 可能滞留"的池上启用。</summary>
    public int IdleReconcileIntervalMs { get; set; }
}
