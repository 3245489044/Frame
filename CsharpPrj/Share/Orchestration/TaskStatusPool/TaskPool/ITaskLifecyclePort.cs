using System.Threading;
using System.Threading.Tasks;

namespace Share.TaskStatusPool.TaskPool;

/// <summary>
/// task 生命周期端口（纯逻辑）：task 状态改变 → 子循环启/停。写入方 = 主循环编排器；消费方 = TaskPool 子循环调度器。
/// <para>零领域依赖；通用实现见 <see cref="TaskPoolLifecyclePort{TItem,TResult}"/>。</para>
/// </summary>
public interface ITaskLifecyclePort
{
    /// <summary>建 task 簿记（NotStarted，不激活消费）。</summary>
    Task CreateTaskAsync(string taskKey, string? input = null, CancellationToken ct = default);

    /// <summary>建 task 簿记（携带子智能体标签与执行器；缺省实现忽略两者，保持旧行为）。</summary>
    Task CreateTaskAsync(string taskKey, string? input, string? subAgentTag, string? executorId, CancellationToken ct = default)
        => CreateTaskAsync(taskKey, input, ct);

    /// <summary>激活 task（→Running，触发子循环启动）。</summary>
    Task ActivateTaskAsync(string taskKey, CancellationToken ct = default);

    /// <summary>暂停 task（→Paused，子循环在调用门挂起）。</summary>
    Task PauseTaskAsync(string taskKey, CancellationToken ct = default);

    /// <summary>恢复 task（→Running，子循环继续）。</summary>
    Task ResumeTaskAsync(string taskKey, CancellationToken ct = default);

    /// <summary>终止 task（→终端，子循环停止）。</summary>
    Task TerminateTaskAsync(string taskKey, CancellationToken ct = default);
}
